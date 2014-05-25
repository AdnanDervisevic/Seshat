#region File Description
/*
 * EstimatedTiming
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using Seshat.Structure;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BinaryAnalysis.UnidecodeSharp;
using System.Xml;
using System.Windows;
using System.Security.Cryptography;
using System.Collections.Generic;
using Seshat.Helpers;
using System.Threading;
using System.Diagnostics;
#endregion

namespace Seshat.Tab
{
    public sealed class EstimatedTimer : IDisposable
    {
        #region Fields

        private object xmlLock = null;

        private CancellationTokenSource cancelToken;
        private List<AudioFile> mp3Files = null;
        private Book book = null;
        private Chapter lastChapter = null;
        private AudioBookStructure structure = AudioBookStructure.UNKNOWN;
        private Stopwatch stopwatch = null;

        private long completedSentences = 0;
        private long totalCompletedSentences = 0;
        private long totalAmountOfSentences = 0;

        private double charsPerSecond = 0;
        private double audioLength = 0;

        #endregion

        #region Events

        /// <summary>
        /// Event fired when the gui should update the progress.
        /// </summary>
        public event EventHandler<double> UpdateProgress;

        /// <summary>
        /// Event fired when the gui should update to show that we're saving.
        /// </summary>
        public event EventHandler Saving;

        /// <summary>
        /// Event fired when the engine has completed the search.
        /// </summary>
        public event EventHandler<Book> Completed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new estimated timing instance.
        /// </summary>
        /// <param name="xmlLock">The xml lock.</param>
        public EstimatedTimer(object xmlLock)
        {
            this.xmlLock = xmlLock;
            this.mp3Files = new List<AudioFile>();
            this.cancelToken = new CancellationTokenSource();
            this.stopwatch = new Stopwatch();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a estimated timing file for the book.
        /// </summary>
        /// <param name="book">The book to estimate the timing file for.</param>
        public void Estimate(Book book)
        {
            Task.Run(
            () =>
            {
                try
                {
                    this.book = book;
                    this.stopwatch.Start();
                    this.mp3Files = new List<AudioFile>();
                    foreach (AudioFile audioFile in book.AudioFiles)
                    {
                        if (cancelToken.Token.IsCancellationRequested)
                            cancelToken.Token.ThrowIfCancellationRequested();
                        this.mp3Files.Add(new AudioFile(audioFile.FileName));
                    }

                    bool chapterStructure = false;
                    int i = 0;
                    int count = 0;
                    foreach (Chapter chapter in book.Chapters)
                    {
                        string[] titleParts = chapter.Title.Split(':');
                        string chapterTitle = titleParts[titleParts.Length - 1].Unidecode().RemoveChars('´', '`', '"', '’', '‘', '“', '”', ';', ',', '،', '、', '″', '~', '*');

                        if (!chapterStructure)
                            i = 0;

                        for (; i < this.mp3Files.Count; i++)
                        {
                            string targetFile = Path.GetFileNameWithoutExtension(this.mp3Files[i].FileName).Unidecode();
                            chapterStructure = false;

                            if (Regex.IsMatch(targetFile, chapterTitle, RegexOptions.IgnoreCase))
                            {
                                chapterStructure = true;
                                count++;

                                foreach (Sentence sentence in chapter.Sentences)
                                    sentence.FirstAudioPosition.AudioFileIndex = i;

                                break;
                            }
                        }
                    }

                    if (chapterStructure)
                    {
                        this.structure = AudioBookStructure.CHAPTER_STRUCTURE;
                        EstimateChapterStructure();
                    }
                    else
                    {
                        this.structure = AudioBookStructure.MULTI_FILES_STRUCTURE;
                        EstimateNonChapterStructure();
                    }
                }
                catch (Exception) 
                {
                    for (int i = 0; i < this.mp3Files.Count; i++)
                        this.mp3Files[i].Dispose(true);
                }
            }, cancelToken.Token);
        }

        /// <summary>
        /// Disposes the estimated timing instance.
        /// </summary>
        public void Dispose()
        {
            this.cancelToken.Cancel();
            this.stopwatch.Stop();
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Estimating all the sentences from the specified book with chapter structure.
        /// </summary>
        private async void EstimateChapterStructure()
        {
            try
            {
                string timingsFile = MainWindow.TimingsFolder + @"\" + this.book.Checksum + ".xml";

                double totalAudioLength = 0;
                for (int i = 0; i < this.mp3Files.Count; i++)
                    totalAudioLength += this.mp3Files[i].TotalTime.TotalSeconds;

                long totalCharCount = 0;
                foreach (Chapter chapter in this.book.Chapters)
                    if (chapter.Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter)
                        totalCharCount += chapter.CharCount;

                double totalCharsPerSecond = (double)totalCharCount / totalAudioLength;

                foreach (Chapter chapter in book.Chapters)
                {
                    if (cancelToken.Token.IsCancellationRequested)
                        cancelToken.Token.ThrowIfCancellationRequested();

                    if (chapter.Sentences.Count < SpeechTimer.MiniumSentencesPerChapter || chapter.Sentences[0].FirstAudioPosition.AudioFileIndex == -1)
                        continue;

                    this.totalAmountOfSentences += chapter.Sentences.Count;
                    this.audioLength = this.mp3Files[chapter.Sentences[0].FirstAudioPosition.AudioFileIndex].TotalTime.TotalSeconds;
                    long charCount = chapter.CharCount;

                    this.charsPerSecond = (double)charCount / this.audioLength; 

                    // Estimate position
                    EstimateChapter(chapter);

                    this.completedSentences = 0;
                    this.totalCompletedSentences += chapter.Sentences.Count;

                    if (this.UpdateProgress != null)
                        this.UpdateProgress(this, (100 / book.GetAmountOfSentencesWithAudioFiles()) * (completedSentences + totalCompletedSentences));

                    this.lastChapter = chapter;
                }

                if (this.Saving != null)
                {
                    this.Saving(this, EventArgs.Empty);
                    this.Saving(this, EventArgs.Empty);
                }

                List<string> audioHashes = new List<string>();
                await Task.Run(() =>
                {
                    try
                    {
                        using (MD5 md5 = MD5.Create())
                        {
                            foreach (AudioFile audioFile in this.mp3Files)
                            {
                                if (cancelToken.Token.IsCancellationRequested)
                                    cancelToken.Token.ThrowIfCancellationRequested();

                                audioHashes.Add(BitConverter.ToString(md5.ComputeHash(audioFile)));
                            }
                        }
                    }
                    catch (Exception) { }
                }, cancelToken.Token);

                if (cancelToken.Token.IsCancellationRequested)
                    cancelToken.Token.ThrowIfCancellationRequested();

                this.stopwatch.Stop();

                lock (xmlLock)
                {
                    XmlDocument doc = new XmlDocument();
                    XmlDeclaration dec = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
                    doc.AppendChild(dec);

                    XmlElement root = doc.CreateElement("Books");
                    doc.AppendChild(root);

                    XmlElement bookElement = doc.CreateElement("Book");
                    bookElement.SetAttribute("CompletedTime", Convert.ToString(stopwatch.Elapsed));
                    bookElement.SetAttribute("Successrate", Convert.ToString(book.Successrate));
                    bookElement.SetAttribute("CharactersPerSecond", Convert.ToString(totalCharsPerSecond));
                    bookElement.SetAttribute("Volume", "0");

                    bool indexSet = false;
                    for (int i = 0; i < this.book.Chapters.Count; i++)
                    {
                        if (this.book.Chapters[i].Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter && this.book.Chapters[i].Sentences[0].FirstAudioPosition.AudioFileIndex >= 0)
                        {
                            bookElement.SetAttribute("CurrentAudioFileIndex", Convert.ToString(this.book.Chapters[i].Sentences[0].FirstAudioPosition.AudioFileIndex));
                            indexSet = true;
                            break;
                        }
                    }

                    if (!indexSet)
                        bookElement.SetAttribute("CurrentAudioFileIndex", "0");

                    bookElement.SetAttribute("CurrentAudioPosition", "00:00:00.0000000");
                    bookElement.SetAttribute("ScrollValue", "0");
                    root.AppendChild(bookElement);

                    XmlElement audioFilesElement = doc.CreateElement("AudioFiles");
                    for (int i = 0; i < this.mp3Files.Count; i++)
                    {
                        XmlElement audioFileElement = doc.CreateElement("AudioFile");
                        audioFileElement.SetAttribute("Path", this.mp3Files[i].FileName);
                        audioFileElement.SetAttribute("Checksum", audioHashes[i]);

                        audioFilesElement.AppendChild(audioFileElement);
                    }
                    bookElement.AppendChild(audioFilesElement);

                    XmlElement chaptersElement = doc.CreateElement("Chapters");
                    foreach (Chapter chapter in book.Chapters)
                    {
                        if (chapter.Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter)
                        {
                            XmlElement chapterElement = doc.CreateElement("Chapter");
                            chapterElement.SetAttribute("Successrate", Convert.ToString(chapter.Successrate));

                            foreach (Sentence sentence in chapter.Sentences)
                            {
                                XmlElement sentenceElement = doc.CreateElement("Sentence");

                                sentenceElement.SetAttribute("AudioFileIndex", Convert.ToString(sentence.FirstAudioPosition.AudioFileIndex));
                                sentenceElement.SetAttribute("AudioPosition", Convert.ToString(sentence.FirstAudioPosition.Position));
                                sentenceElement.SetAttribute("Duration", Convert.ToString(sentence.FirstAudioPosition.Duration));

                                chapterElement.AppendChild(sentenceElement);
                            }

                            chaptersElement.AppendChild(chapterElement);
                        }
                    }

                    bookElement.AppendChild(chaptersElement);

                    doc.Save(timingsFile);
                }

                if (this.Completed != null)
                    this.Completed(this, book);
            }
            catch (Exception) 
            {
                for (int i = 0; i < this.mp3Files.Count; i++)
                    this.mp3Files[i].Dispose(true);
            }
        }

        /// <summary>
        /// Estimating all the sentences from the specified book with a non chapter structure.
        /// </summary>
        private async void EstimateNonChapterStructure()
        {
            try
            {
                string timingsFile = MainWindow.TimingsFolder + @"\" + this.book.Checksum + ".xml";

                for (int i = 0; i < this.mp3Files.Count; i++)
                    this.audioLength += this.mp3Files[i].TotalTime.TotalSeconds;

                long charCount = 0;
                foreach (Chapter chapter in this.book.Chapters)
                    if (chapter.Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter)
                        charCount += chapter.CharCount;

                this.charsPerSecond = (double)charCount / this.audioLength;

                foreach (Chapter chapter in book.Chapters)
                {
                    if (cancelToken.Token.IsCancellationRequested)
                        cancelToken.Token.ThrowIfCancellationRequested();

                    if (chapter.Sentences.Count < SpeechTimer.MiniumSentencesPerChapter)
                        continue;

                    this.totalAmountOfSentences += chapter.Sentences.Count;

                    // Estimate position
                    EstimateChapter(chapter);

                    this.completedSentences = 0;
                    this.totalCompletedSentences += chapter.Sentences.Count;

                    if (this.UpdateProgress != null)
                        this.UpdateProgress(this, (100 / book.GetAmountOfSentencesWithAudioFiles()) * (completedSentences + totalCompletedSentences));

                    this.lastChapter = chapter;
                }

                if (this.Saving != null)
                {
                    this.Saving(this, EventArgs.Empty);
                    this.Saving(this, EventArgs.Empty);
                }

                List<string> audioHashes = new List<string>();
                await Task.Run(() =>
                {
                    try
                    {
                        using (MD5 md5 = MD5.Create())
                        {
                            foreach (AudioFile audioFile in this.mp3Files)
                            {
                                if (cancelToken.Token.IsCancellationRequested)
                                    cancelToken.Token.ThrowIfCancellationRequested();

                                audioHashes.Add(BitConverter.ToString(md5.ComputeHash(audioFile)));
                            }
                        }
                    }
                    catch (Exception) { }
                }, cancelToken.Token);

                if (cancelToken.Token.IsCancellationRequested)
                    cancelToken.Token.ThrowIfCancellationRequested();

                this.stopwatch.Stop();

                lock (xmlLock)
                {
                    XmlDocument doc = new XmlDocument();
                    XmlDeclaration dec = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
                    doc.AppendChild(dec);

                    XmlElement root = doc.CreateElement("Books");
                    doc.AppendChild(root);

                    XmlElement bookElement = doc.CreateElement("Book");
                    bookElement.SetAttribute("CompletedTime", Convert.ToString(stopwatch.Elapsed));
                    bookElement.SetAttribute("Successrate", Convert.ToString(book.Successrate));
                    bookElement.SetAttribute("CharactersPerSecond", Convert.ToString(this.charsPerSecond));
                    bookElement.SetAttribute("Volume", "0");

                    bool indexSet = false;
                    for (int i = 0; i < this.book.Chapters.Count; i++)
                    {
                        if (this.book.Chapters[i].Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter && this.book.Chapters[i].Sentences[0].FirstAudioPosition.AudioFileIndex >= 0)
                        {
                            bookElement.SetAttribute("CurrentAudioFileIndex", Convert.ToString(this.book.Chapters[i].Sentences[0].FirstAudioPosition.AudioFileIndex));
                            indexSet = true;
                            break;
                        }
                    }

                    if (!indexSet)
                        bookElement.SetAttribute("CurrentAudioFileIndex", "0");

                    bookElement.SetAttribute("CurrentAudioPosition", "00:00:00.0000000");
                    bookElement.SetAttribute("ScrollValue", "0");
                    root.AppendChild(bookElement);

                    XmlElement audioFilesElement = doc.CreateElement("AudioFiles");
                    for (int i = 0; i < this.mp3Files.Count; i++)
                    {
                        XmlElement audioFileElement = doc.CreateElement("AudioFile");
                        audioFileElement.SetAttribute("Path", this.mp3Files[i].FileName);
                        audioFileElement.SetAttribute("Checksum", audioHashes[i]);

                        audioFilesElement.AppendChild(audioFileElement);
                    }

                    bookElement.AppendChild(audioFilesElement);

                    XmlElement chaptersElement = doc.CreateElement("Chapters");
                    foreach (Chapter chapter in book.Chapters)
                    {
                        if (chapter.Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter)
                        {
                            XmlElement chapterElement = doc.CreateElement("Chapter");
                            chapterElement.SetAttribute("Successrate", Convert.ToString(chapter.Successrate));
                            foreach (Sentence sentence in chapter.Sentences)
                            {
                                XmlElement sentenceElement = doc.CreateElement("Sentence");

                                sentenceElement.SetAttribute("AudioFileIndex", Convert.ToString(sentence.FirstAudioPosition.AudioFileIndex));
                                sentenceElement.SetAttribute("AudioPosition", Convert.ToString(sentence.FirstAudioPosition.Position));
                                sentenceElement.SetAttribute("Duration", Convert.ToString(sentence.FirstAudioPosition.Duration));

                                chapterElement.AppendChild(sentenceElement);
                            }

                            chaptersElement.AppendChild(chapterElement);
                        }
                    }

                    bookElement.AppendChild(chaptersElement);

                    doc.Save(timingsFile);
                }

                if (this.Saving != null)
                    this.Saving(this, EventArgs.Empty);

                if (this.Completed != null)
                    this.Completed(this, book);
            }
            catch (Exception) 
            {
                for (int i = 0; i < this.mp3Files.Count; i++)
                    this.mp3Files[i].Dispose();
            }
        }

        /// <summary>
        /// Estimates a chapter.
        /// </summary>
        /// <param name="chapter">The chapter to estimate</param>
        private void EstimateChapter(Chapter chapter)
        {
            int i = 0;
            foreach (Sentence sentence in chapter.Sentences)
            {
                if (cancelToken.Token.IsCancellationRequested)
                    break;

                sentence.FirstAudioPosition.Position = TimeSpan.FromSeconds(EstimatePositionInSeconds(i, chapter));
                sentence.FirstAudioPosition.Duration = TimeSpan.FromSeconds(sentence.CharCount / this.charsPerSecond);

                if (this.structure != AudioBookStructure.CHAPTER_STRUCTURE)
                {
                    TimeSpan time = sentence.FirstAudioPosition.Position;

                    int audioFileIndex = 0;
                    for (audioFileIndex = 0; audioFileIndex < this.mp3Files.Count; audioFileIndex++)
                    {
                        time -= this.mp3Files[audioFileIndex].TotalTime;

                        if (time < TimeSpan.Zero)
                            break;
                    }

                    if (this.mp3Files.Count > audioFileIndex)
                        sentence.FirstAudioPosition.AudioFileIndex = audioFileIndex;
                    else
                        sentence.FirstAudioPosition.AudioFileIndex = this.mp3Files.Count - 1;
                }
                i++;
            }
        }

        /// <summary>
        /// Estimates the position in seconds.
        /// </summary>
        /// <param name="sentenceIndex">The sentence index in the chapter.</param>
        /// <param name="chapter">The chapter.</param>
        /// <param name="offset">The offset to add.</param>
        /// <returns>Returns the position in seconds.</returns>
        private double EstimatePositionInSeconds(int sentenceIndex, Chapter chapter)
        {
            double charCount = 0;
            for (int i = 0; i < sentenceIndex; i++)
                charCount += chapter.Sentences[i].CharCount;

            if (lastChapter == null)
                return (charCount / this.charsPerSecond);
            else
            {
                if (this.structure == AudioBookStructure.CHAPTER_STRUCTURE)
                {
                    double totalSeconds = 0;
                    for (int i = 0; i < chapter.Sentences[0].FirstAudioPosition.AudioFileIndex; i++)
                        totalSeconds += this.mp3Files[i].TotalTime.TotalSeconds;

                    return (charCount / this.charsPerSecond) + totalSeconds;
                }
                else
                {
                    Sentence lastSentence = lastChapter.GetLastSentenceWithValues();
                    if (lastSentence == null)
                        return (charCount / this.charsPerSecond);
                    else
                        return (charCount / this.charsPerSecond) + lastSentence.FirstAudioPosition.TotalSeconds();
                }
            }
        }

        /// <summary>
        /// Helper for writing debug messages.
        /// </summary>
        private void DebugWriteLine(Chapter chapter, string header)
        {
            System.Diagnostics.Debug.WriteLine("###################" + header + "#####################");

            System.Diagnostics.Debug.WriteLine("ID # Position ### Duration ### Text");

            for (int i = 0; i < chapter.Sentences.Count; i++)
            {
                System.Diagnostics.Debug.Write(i);
                System.Diagnostics.Debug.Write(" - ");
                System.Diagnostics.Debug.Write(chapter.Sentences[i].FirstAudioPosition.Position);
                System.Diagnostics.Debug.Write(" - ");
                System.Diagnostics.Debug.Write(chapter.Sentences[i].FirstAudioPosition.Duration);
                System.Diagnostics.Debug.Write(" - ");
                System.Diagnostics.Debug.WriteLine(chapter.Sentences[i].Text);
            }

            System.Diagnostics.Debug.WriteLine("########################################");
        }

        #endregion
    }
}