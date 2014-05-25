#region File Description
/*
 * SpeechTimer
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.IO;
using System.Xml;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Text.RegularExpressions;
using NAudio.Wave;
using Seshat.Structure;
using Seshat.Helpers;
using BinaryAnalysis.UnidecodeSharp;
using System.Security.Cryptography;
using System.Diagnostics;
#endregion

namespace Seshat.Tab
{
    /// <summary>
    /// Different timing type errors.
    /// </summary>
    public enum TimingType
    {
        NORMAL,
        MISSING_AUDIO,
        CORRUPT,
        PROGRESS
    }

    /// <summary>
    /// Class for creating the timing file.
    /// </summary>
    public sealed class SpeechTimer : IDisposable
    {
        #region Consts

        public const int MiniumSentencesPerChapter = 20;

        #endregion

        #region Fields

        private object xmlLock = null;
        
        private CancellationTokenSource cancelToken;
        private Book book = null;
        private Chapter currentChapter = null;
        private Chapter lastChapter = null;

        private Stopwatch stopwatch = null;
        private AudioBookStructure structure = AudioBookStructure.UNKNOWN;

        private SpeechStream speechStream = null;
        private List<AudioFile> mp3Files = null;
        private List<WaveFormatConversionStream> audioFiles = null;

        private long completedSentences = 0;
        private long totalCompletedSentences = 0;
        private long totalAmountOfSentences = 0;

        private TimeSpan timeToSkip;
        private double charsPerSecond = 0;
        private double audioLength = 0;
        private bool useMp3File = false;

        private SpeechRecognitionEngine speechEngine = null;
        private TaskManualResetEvent resetEvent = null;

        private uint successCounter = 0;

        #endregion

        #region Events

        /// <summary>
        /// Event fired when the gui should update the progress.
        /// </summary>
        public event EventHandler<double> UpdateProgress;

        /// <summary>
        /// Event fired when something is wrong.
        /// </summary>
        public event EventHandler<string> Exception;

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
        /// Constructs a new Speech Timer instance.
        /// </summary>
        public SpeechTimer(object xmlLock)
        {
            this.xmlLock = xmlLock;
            this.mp3Files = new List<AudioFile>();
            this.audioFiles = new List<WaveFormatConversionStream>();

            speechEngine = new SpeechRecognitionEngine();
            speechEngine.SpeechRecognized += speechEngine_SpeechRecognized;
            speechEngine.AudioStateChanged += speechEngine_AudioStateChanged;
            resetEvent = new TaskManualResetEvent(false);
            this.cancelToken = new CancellationTokenSource();
            this.stopwatch = new Stopwatch();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Load the existing timing file if it exists.
        /// </summary>
        /// <param name="book">The book.</param>
        /// <returns>True if the timing file was loaded; otherwise false.</returns>
        public TimingType LoadExisting(Book book)
        {
            TimingType result = TimingType.NORMAL;
            string timingsFile = MainWindow.TimingsFolder + @"\" + book.Checksum + ".xml";

            lock (xmlLock)
            {
                if (File.Exists(timingsFile))
                {
                    try
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.Load(timingsFile);

                        book.Successrate = Convert.ToSingle(((XmlElement)xmlDoc.GetElementsByTagName("Book")[0]).GetAttribute("Successrate"));

                        XmlNodeList audioFilesList = xmlDoc.GetElementsByTagName("AudioFile");
                        foreach (XmlElement audioFileElement in audioFilesList)
                        {
                            if (File.Exists(audioFileElement.GetAttribute("Path")))
                                book.AudioFiles.Add(new AudioFile(audioFileElement.GetAttribute("Path")));
                        }

                        if (book.AudioFiles.Count != audioFilesList.Count)
                            result = TimingType.MISSING_AUDIO;

                        XmlNodeList chapterList = xmlDoc.GetElementsByTagName("Chapter");
                        if (chapterList.Count != book.Chapters.Count)
                            throw new Exception("Corrupt timing file.");

                        int i = 0;
                        foreach (XmlElement chapterNode in chapterList)
                        {
                            int j = 0;
                            XmlNodeList sentenceList = chapterNode.GetElementsByTagName("Sentence");

                            if (sentenceList.Count != book.Chapters[i].Sentences.Count)
                                throw new Exception("Corrupt timing file.");

                            foreach (XmlElement sentenceNode in sentenceList)
                            {
                                book.Chapters[i].Sentences[j].FirstAudioPosition = new AudioPosition()
                                {
                                    Position = TimeSpan.Parse(sentenceNode.GetAttribute("AudioPosition")),
                                    Duration = TimeSpan.Parse(sentenceNode.GetAttribute("Duration")),
                                    AudioFileIndex = Convert.ToInt32(sentenceNode.GetAttribute("AudioFileIndex"))
                                };

                                j++;
                            }

                            i++;
                        }

                        return result;
                    }
                    catch (Exception ex) 
                    {
                        if (this.Exception != null)
                            this.Exception(this, ex.Message);
                    }

                    MessageBox.Show("The timing file is corrupt, it will be removed,\ncopy it if you want to save it before you click OK.", "ERROR!", MessageBoxButton.OK, MessageBoxImage.Error);
                    File.Delete(timingsFile);
                }
            }

            return TimingType.CORRUPT;
        }

        /// <summary>
        /// Starts recognizes a book.
        /// </summary>
        /// <param name="book">The book.</param>
        public async Task Recognize(Book book)
        {
            this.book = book;
            stopwatch.Start();
            this.mp3Files = new List<AudioFile>();
            foreach (AudioFile audioFile in book.AudioFiles)
                this.mp3Files.Add(new AudioFile(audioFile.FileName));

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

            if (count != this.mp3Files.Count)
                chapterStructure = false;

            if (chapterStructure)
            {
                this.structure = AudioBookStructure.CHAPTER_STRUCTURE;
                RecognizeChapterStructure();
            }
            else
            {
                this.structure = AudioBookStructure.MULTI_FILES_STRUCTURE;
                RecognizeNonChapterStructure();
            }
        }

        /// <summary>
        /// Disposes the speech timer.
        /// </summary>
        public void Dispose()
        {
            this.cancelToken.Cancel();
            this.speechEngine.Dispose();
            this.resetEvent.Dispose();
            this.stopwatch.Stop();
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// ecognizes all the sentences from the specified book with chapter structure. 
        /// </summary>
        private async Task RecognizeChapterStructure()
        {
            try
            {
                string timingsFile = MainWindow.TimingsFolder + @"\" + this.book.Checksum + ".xml";

                this.useMp3File = true;

                for (int i = 0; i < this.mp3Files.Count; i++)
                    if (this.mp3Files[i].WaveFormat.BitsPerSample != 44100 || this.mp3Files[i].WaveFormat.SampleRate != 16)
                        this.useMp3File = false;

                if (!this.useMp3File)
                {
                    for (int i = 0; i < this.mp3Files.Count; i++)
                    {
                        this.audioFiles.Add(new WaveFormatConversionStream(new WaveFormat(44100, 16, 1), WaveFormatConversionStream.CreatePcmStream(this.mp3Files[i])));
                        if (this.audioFiles[i].TotalTime < TimeSpan.Zero)
                            break;
                    }

                    if (this.mp3Files.Count != this.audioFiles.Count)
                    {
                        this.speechStream.Close();

                        for (int i = 0; i < this.mp3Files.Count; i++)
                            this.mp3Files[i].Dispose(true);
                        this.mp3Files.Clear();

                        this.book.AudioFiles.Clear();

                        for (int i = 0; i < this.audioFiles.Count; i++)
                            this.audioFiles[i].Dispose();
                        this.audioFiles.Clear();

                        speechEngine.SetInputToNull();
                        speechEngine.SpeechRecognized -= speechEngine_SpeechRecognized;
                        speechEngine.AudioStateChanged -= speechEngine_AudioStateChanged;
                        speechEngine.Dispose();

                        if (this.Exception != null)
                            this.Exception(this, "This application does not support one or more of these mp3 files.");

                        return;
                    }
                }

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
                    if (chapter.Sentences.Count < SpeechTimer.MiniumSentencesPerChapter || chapter.Sentences[0].FirstAudioPosition.AudioFileIndex == -1)
                        continue;

                    this.totalAmountOfSentences += chapter.Sentences.Count;
                    this.currentChapter = chapter;

                    this.audioLength = this.mp3Files[chapter.Sentences[0].FirstAudioPosition.AudioFileIndex].TotalTime.TotalSeconds;
                    long charCount = chapter.CharCount;

                    this.charsPerSecond = (double)charCount / this.audioLength;
                    this.speechStream = new SpeechStream(33554432);

                    if (this.useMp3File)
                    {
                        for (int i = 0; i < this.mp3Files.Count; i++)
                            this.mp3Files[i].Position = 0;

                        speechEngine.SetInputToAudioStream(speechStream, new SpeechAudioFormatInfo(this.mp3Files[0].WaveFormat.SampleRate, (AudioBitsPerSample)this.mp3Files[0].WaveFormat.BitsPerSample, (AudioChannel)this.mp3Files[0].WaveFormat.Channels));
                    }
                    else
                    {
                        for (int i = 0; i < this.audioFiles.Count; i++)
                            this.audioFiles[i].Position = 0;

                        speechEngine.SetInputToAudioStream(speechStream, new SpeechAudioFormatInfo(this.audioFiles[0].WaveFormat.SampleRate, (AudioBitsPerSample)this.audioFiles[0].WaveFormat.BitsPerSample, (AudioChannel)this.audioFiles[0].WaveFormat.Channels));
                    }

                    if (this.speechEngine.Grammars.Count > 0)
                        this.speechEngine.UnloadAllGrammars();

                    for (int i = 0; i < this.currentChapter.Sentences.Count; i++)
                        if (currentChapter.Sentences[i].Grammar != null)
                            this.speechEngine.LoadGrammar(currentChapter.Sentences[i].Grammar);

                    this.speechEngine.RecognizeAsync(RecognizeMode.Multiple);

                    ThreadPool.QueueUserWorkItem(ReadMp3File);
                    await resetEvent.WaitOne();
                    resetEvent.Reset();

                    this.speechStream.Close();

                    speechEngine.SetInputToNull();
                    speechEngine.SpeechRecognized -= speechEngine_SpeechRecognized;
                    speechEngine.AudioStateChanged -= speechEngine_AudioStateChanged;
                    speechEngine.Dispose();

                    if (!this.cancelToken.Token.IsCancellationRequested)
                    {
                        speechEngine = new SpeechRecognitionEngine();
                        speechEngine.SpeechRecognized += speechEngine_SpeechRecognized;
                        speechEngine.AudioStateChanged += speechEngine_AudioStateChanged;

                        this.completedSentences = 0;
                        this.totalCompletedSentences += chapter.Sentences.Count;

                        if (UpdateProgress != null)
                            this.UpdateProgress(this, (100 / book.GetAmountOfSentencesWithAudioFiles()) * (completedSentences + totalCompletedSentences));

                        this.lastChapter = chapter;
                    }
                    else
                        break;
                }

                if (this.Saving != null)
                {
                    this.Saving(this, EventArgs.Empty);
                    this.Saving(this, EventArgs.Empty);
                }

                if (this.cancelToken.Token.IsCancellationRequested)
                {
                    for (int i = 0; i < this.audioFiles.Count; i++)
                        this.audioFiles[i].Dispose();
                    this.audioFiles.Clear();

                    for (int i = 0; i < this.mp3Files.Count; i++)
                        this.mp3Files[i].Dispose();
                    this.mp3Files.Clear();
                    return;
                }

                book.Successrate = (float)Math.Round((this.successCounter / (double)this.totalAmountOfSentences) * 100);

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
                                audioFile.Position = 0;
                                audioHashes.Add(BitConverter.ToString(md5.ComputeHash(audioFile)));
                            }
                        }
                    }
                    catch (Exception) { }
                }, cancelToken.Token);

                if (cancelToken.Token.IsCancellationRequested)
                    cancelToken.Token.ThrowIfCancellationRequested();

                this.stopwatch.Stop();

                // Save timings into files.
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

                for (int i = 0; i < this.audioFiles.Count; i++)
                    this.audioFiles[i].Dispose();
                this.audioFiles.Clear();

                for (int i = 0; i < this.mp3Files.Count; i++)
                    this.mp3Files[i].Dispose(true);
                this.mp3Files.Clear();

                if (this.Completed != null)
                    this.Completed(this, book);

                return;
            }
            catch (Exception ex)
            {
                for (int i = 0; i < this.audioFiles.Count; i++)
                    this.audioFiles[i].Dispose();
                this.audioFiles.Clear();

                for (int i = 0; i < this.mp3Files.Count; i++)
                    this.mp3Files[i].Dispose(true);
                this.mp3Files.Clear();
            }
        }

        /// <summary>
        /// Recognizes all the sentences from the specified book 
        /// </summary>
        private async Task RecognizeNonChapterStructure()
        {
            try
            {
                string timingsFile = MainWindow.TimingsFolder + @"\" + this.book.Checksum + ".xml";

                for (int i = 0; i < this.book.AudioFiles.Count; i++)
                    this.audioLength += this.mp3Files[i].TotalTime.TotalSeconds;

                this.useMp3File = true;

                for (int i = 0; i < this.mp3Files.Count; i++)
                    if (this.mp3Files[i].WaveFormat.SampleRate != 44100 || this.mp3Files[i].WaveFormat.BitsPerSample != 16)
                        this.useMp3File = false;

                if (!this.useMp3File)
                {
                    for (int i = 0; i < this.mp3Files.Count; i++)
                    {
                        this.audioFiles.Add(new WaveFormatConversionStream(new WaveFormat(44100, 16, 1), WaveFormatConversionStream.CreatePcmStream(this.mp3Files[i])));
                        if (this.audioFiles[i].TotalTime < TimeSpan.Zero)
                            break;
                    }

                    if (this.mp3Files.Count != this.audioFiles.Count)
                    {
                        this.speechStream.Close();

                        for (int i = 0; i < this.mp3Files.Count; i++)
                            this.mp3Files[i].Dispose(true);
                        this.mp3Files.Clear();

                        this.book.AudioFiles.Clear();

                        for (int i = 0; i < this.audioFiles.Count; i++)
                            this.audioFiles[i].Dispose();
                        this.audioFiles.Clear();

                        speechEngine.SetInputToNull();
                        speechEngine.SpeechRecognized -= speechEngine_SpeechRecognized;
                        speechEngine.AudioStateChanged -= speechEngine_AudioStateChanged;
                        speechEngine.Dispose();

                        if (this.Exception != null)
                            this.Exception(this, "This application does not support one or more of these mp3 files.");

                        return;
                    }
                }

                long charCount = 0;

                foreach (Chapter chapter in this.book.Chapters)
                    if (chapter.Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter)
                        charCount += chapter.CharCount;

                this.charsPerSecond = (double)charCount / this.audioLength;

                foreach (Chapter chapter in book.Chapters)
                {
                    if (chapter.Sentences.Count < SpeechTimer.MiniumSentencesPerChapter)
                        continue;

                    this.totalAmountOfSentences += (uint)chapter.Sentences.Count;
                    this.currentChapter = chapter;

                    this.speechStream = new SpeechStream(33554432);

                    if (this.useMp3File)
                    {
                        for (int i = 0; i < this.mp3Files.Count; i++)
                            this.mp3Files[i].Position = 0;

                        speechEngine.SetInputToAudioStream(speechStream, new SpeechAudioFormatInfo(this.mp3Files[0].WaveFormat.SampleRate, (AudioBitsPerSample)this.mp3Files[0].WaveFormat.BitsPerSample, (AudioChannel)this.mp3Files[0].WaveFormat.Channels));
                    }
                    else
                    {
                        for (int i = 0; i < this.audioFiles.Count; i++)
                            this.audioFiles[i].Position = 0;

                        speechEngine.SetInputToAudioStream(speechStream, new SpeechAudioFormatInfo(this.audioFiles[0].WaveFormat.SampleRate, (AudioBitsPerSample)this.audioFiles[0].WaveFormat.BitsPerSample, (AudioChannel)this.audioFiles[0].WaveFormat.Channels));
                    }

                    if (this.speechEngine.Grammars.Count > 0)
                        this.speechEngine.UnloadAllGrammars();

                    for (int i = 0; i < this.currentChapter.Sentences.Count; i++)
                        if (currentChapter.Sentences[i].Grammar != null)
                            this.speechEngine.LoadGrammar(currentChapter.Sentences[i].Grammar);

                    this.speechEngine.RecognizeAsync(RecognizeMode.Multiple);

                    ThreadPool.QueueUserWorkItem(ReadMp3File);
                    await resetEvent.WaitOne();
                    resetEvent.Reset();

                    this.speechStream.Close();

                    speechEngine.SetInputToNull();
                    speechEngine.SpeechRecognized -= speechEngine_SpeechRecognized;
                    speechEngine.AudioStateChanged -= speechEngine_AudioStateChanged;
                    speechEngine.Dispose();

                    if (!this.cancelToken.Token.IsCancellationRequested)
                    {
                        speechEngine = new SpeechRecognitionEngine();
                        speechEngine.SpeechRecognized += speechEngine_SpeechRecognized;
                        speechEngine.AudioStateChanged += speechEngine_AudioStateChanged;

                        this.completedSentences = 0;
                        this.totalCompletedSentences += chapter.Sentences.Count;

                        if (UpdateProgress != null)
                            this.UpdateProgress(this, (100 / book.GetAmountOfSentencesWithAudioFiles()) * (completedSentences + totalCompletedSentences));

                        this.lastChapter = chapter;
                    }
                    else
                        break;
                }

                if (this.Saving != null)
                {
                    this.Saving(this, EventArgs.Empty);
                    this.Saving(this, EventArgs.Empty);
                }

                if (this.cancelToken.Token.IsCancellationRequested)
                {
                    for (int i = 0; i < this.audioFiles.Count; i++)
                        this.audioFiles[i].Dispose();
                    this.audioFiles.Clear();

                    for (int i = 0; i < this.mp3Files.Count; i++)
                        this.mp3Files[i].Dispose();
                    this.mp3Files.Clear();
                    return;
                }

                book.Successrate = (float)Math.Round((this.successCounter / (double)this.totalAmountOfSentences) * 100);

                List<string> audioHashes = new List<string>();
                await Task.Run(() =>
                {
                    try
                    {
                        using (MD5 md5 = MD5.Create())
                        {
                            for (int i = 0; i < this.mp3Files.Count; i++)
                            {
                                if (cancelToken.Token.IsCancellationRequested)
                                    cancelToken.Token.ThrowIfCancellationRequested();

                                this.mp3Files[i].Position = 0;
                                audioHashes.Add(BitConverter.ToString(md5.ComputeHash(this.mp3Files[i])));
                            }
                        }
                    }
                    catch (Exception) { }
                }, cancelToken.Token);

                if (cancelToken.Token.IsCancellationRequested)
                    cancelToken.Token.ThrowIfCancellationRequested();

                this.stopwatch.Stop();

                // Save timings into files.
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

                for (int i = 0; i < this.audioFiles.Count; i++)
                    this.audioFiles[i].Dispose();
                this.audioFiles.Clear();

                for (int i = 0; i < this.mp3Files.Count; i++)
                    this.mp3Files[i].Dispose(true);
                this.mp3Files.Clear();

                if (this.Completed != null)
                    this.Completed(this, book);

                return;
            }
            catch (Exception ex)
            {
                for (int i = 0; i < this.audioFiles.Count; i++)
                    this.audioFiles[i].Dispose();
                this.audioFiles.Clear();

                for (int i = 0; i < this.mp3Files.Count; i++)
                    this.mp3Files[i].Dispose(true);
                this.mp3Files.Clear();
            }
        }

        /// <summary>
        /// Method that reads the mp3 file and writes it to the voice stream.
        /// </summary>
        private void ReadMp3File(object state)
        {
            if (structure == AudioBookStructure.CHAPTER_STRUCTURE)
            {
                while (true)
                {
                    if (this.cancelToken.Token.IsCancellationRequested)
                        break;

                    byte[] buffer = new byte[1048576];
                    int length = 0;

                    if (useMp3File)
                        length = this.mp3Files[this.currentChapter.Sentences[0].FirstAudioPosition.AudioFileIndex].Read(buffer, 0, buffer.Length);
                    else
                        length = this.audioFiles[this.currentChapter.Sentences[0].FirstAudioPosition.AudioFileIndex].Read(buffer, 0, buffer.Length);

                    if (length == 0)
                    {
                        this.speechStream.EndOfFile = true;
                        break;
                    }

                    speechStream.Write(buffer, 0, length);
                }
            }
            else
            {
                int startMp3 = 0;

                if (this.lastChapter != null)
                {
                    double time = lastChapter.GetLastSentenceWithValues().FirstAudioPosition.TotalSeconds();
                    this.timeToSkip = TimeSpan.FromSeconds(time) - TimeSpan.FromMinutes(10);

                    for (startMp3 = 0; startMp3 < this.mp3Files.Count; startMp3++)
                    {
                        time -= this.mp3Files[startMp3].TotalTime.TotalSeconds;

                        if (time < 0)
                            break;
                    }
                }

                for (int i = startMp3; i < this.mp3Files.Count; i++)
                {
                    if (this.cancelToken.Token.IsCancellationRequested)
                        break;

                    if (this.lastChapter != null && timeToSkip > TimeSpan.Zero && i == startMp3)
                    {
                        if (this.useMp3File)
                        {
                            this.mp3Files[i].CurrentTime = timeToSkip;

                            if (this.mp3Files[i].CurrentTime == TimeSpan.Zero)
                            {
                                for (int j = 0; j < startMp3; j++)
                                    this.timeToSkip += this.mp3Files[j].TotalTime;
                            }
                        }
                        else
                        {
                            this.audioFiles[i].CurrentTime = timeToSkip;

                            if (this.audioFiles[i].CurrentTime == TimeSpan.Zero)
                            {
                                for (int j = 0; j < startMp3; j++)
                                    this.timeToSkip += this.audioFiles[j].TotalTime;
                            }
                        }
                    }
                    else
                        this.timeToSkip = TimeSpan.Zero;

                    TimeSpan breakPoint = this.timeToSkip + TimeSpan.FromHours(3);
                    while (true)
                    {
                        if (this.cancelToken.Token.IsCancellationRequested || this.mp3Files[i].CurrentTime >= breakPoint)
                            break;

                        byte[] buffer = new byte[1048576];
                        int length = 0;

                        if (useMp3File)
                            length = this.mp3Files[i].Read(buffer, 0, buffer.Length);
                        else
                            length = this.audioFiles[i].Read(buffer, 0, buffer.Length);

                        if (length == 0)
                            break;

                        speechStream.Write(buffer, 0, length);
                    }
                }

                this.speechStream.EndOfFile = true;
            }
        }

        /// <summary>
        /// Method fired when the speech engine recognizes a grammar object.
        /// </summary>
        private void speechEngine_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            TimeSpan timeToSkip = this.timeToSkip;

            TimeSpan time = e.Result.Audio.AudioPosition + timeToSkip;
            TimeSpan addTime = TimeSpan.Zero;

            int audioFileIndex = 0;

            if (structure != AudioBookStructure.CHAPTER_STRUCTURE)
            {
                for (audioFileIndex = 0; audioFileIndex < this.mp3Files.Count; audioFileIndex++)
                {
                    time -= this.mp3Files[audioFileIndex].TotalTime;

                    if (time < TimeSpan.Zero)
                        break;
                }
            }
            else
            {
                for (int i = 0; i < this.currentChapter.Sentences[0].FirstAudioPosition.AudioFileIndex; i++)
                    addTime += this.mp3Files[i].TotalTime;

                audioFileIndex = this.currentChapter.Sentences[0].FirstAudioPosition.AudioFileIndex;
            }

            for (int i = 0; i < this.currentChapter.Sentences.Count; i++)
            {
                if (this.currentChapter.Sentences[i].Text == e.Result.Text)
                {
                    if (this.currentChapter.Sentences[i].Grammar == null)
                        continue;

                    if (this.currentChapter.Sentences[i].AudioPositions.Count == 1 && this.currentChapter.Sentences[i].AudioPositions[0].Position == TimeSpan.Zero && this.currentChapter.Sentences[i].AudioPositions[0].Duration == TimeSpan.Zero)
                    {
                        this.currentChapter.Sentences[i].FirstAudioPosition = new AudioPosition()
                        {
                            Position = e.Result.Audio.AudioPosition + addTime + timeToSkip,
                            Duration = e.Result.Audio.Duration,
                            AudioFileIndex = audioFileIndex
                        };
                    }
                    else
                    {
                        this.currentChapter.Sentences[i].AudioPositions.Add(new AudioPosition()
                        {
                            Position = e.Result.Audio.AudioPosition + addTime + timeToSkip,
                            Duration = e.Result.Audio.Duration,
                            AudioFileIndex = audioFileIndex
                        });
                    }

                    completedSentences++;

                    if (UpdateProgress != null)
                        this.UpdateProgress(this, (100 / book.GetAmountOfSentencesWithAudioFiles()) * (completedSentences + totalCompletedSentences));
                }
            }
        }

        /// <summary>
        /// Method fired when the speech engine changes audio state.
        /// </summary>
        private void speechEngine_AudioStateChanged(object sender, AudioStateChangedEventArgs e)
        {
            if (e.AudioState == AudioState.Stopped && !this.cancelToken.Token.IsCancellationRequested)
            {
                TimeSpan offset = TimeSpan.Zero;

                if (this.currentChapter.Sentences.Count > 0)
                {
                    //DebugWriteLine("After speech recognition");

                    for (int i = 0; i < this.currentChapter.Sentences.Count; i++)
                    {
                        // Step one, find the previous position that has a position and duration not set to zeor.
                        AudioPosition previousPosition = new AudioPosition();
                        int decrement = 1;
                        do
                        {
                            // If we're out of range we should break the loop.
                            if (i - decrement < 0)
                            {
                                if (this.lastChapter != null)
                                    previousPosition = this.lastChapter.GetLastSentenceWithValues().FirstAudioPosition;
                                break;
                            }

                            previousPosition = this.currentChapter.Sentences[i - decrement].FirstAudioPosition;
                            decrement++;
                        } while (previousPosition.Position == TimeSpan.Zero || previousPosition.Duration == TimeSpan.Zero);

                        // The second step is to get the closest audio position to the previous position.
                        int audioFileIndex = this.currentChapter.Sentences[i].FirstAudioPosition.AudioFileIndex;
                        AudioPosition closestAudio = this.currentChapter.Sentences[i].GetClosestAudioPosition(previousPosition.Position);
                        closestAudio.AudioFileIndex = audioFileIndex;

                        // The third step is to find the next position that has a position and duration not set to zero.
                        AudioPosition nextPosition = new AudioPosition();
                        if (i != this.currentChapter.Sentences.Count - 1)
                        {
                            int increment = 1;
                            bool lastSentence = false;

                            // Loop until we find a position that appears to be correct.
                            while (true)
                            {
                                do
                                {
                                    // If we'll go out of range next increment, let the program 
                                    // know that we're on the last sentence and break the loop.
                                    if (i + increment >= this.currentChapter.Sentences.Count)
                                    {
                                        lastSentence = true;
                                        break;
                                    }

                                    nextPosition = this.currentChapter.Sentences[i + increment].GetClosestAudioPosition(previousPosition.Position);
                                    increment++;
                                } while (nextPosition.Position == TimeSpan.Zero || nextPosition.Duration == TimeSpan.Zero);

                                // Estimate the interval where the next position should be in.
                                TimeSpan estimatedShouldBe = TimeSpan.FromSeconds(EstimatePositionInSeconds(i + increment, offset));
                                TimeSpan estimatedShouldBeMin = estimatedShouldBe - TimeSpan.FromSeconds(90);
                                TimeSpan estimatedShouldBeMax = estimatedShouldBe + TimeSpan.FromSeconds(90);

                                // Calculate the interval where the next position should be.
                                TimeSpan calculatedShouldBe = closestAudio.Position + closestAudio.Duration;
                                TimeSpan calculatedShouldBeMin = calculatedShouldBe - TimeSpan.FromSeconds(45);
                                TimeSpan calculatedShouldBeMax = calculatedShouldBe + TimeSpan.FromSeconds(45);

                                // If the next position is within the estimated or calculated interval or if we 
                                // are on the last sentence, we have found a position that appears to be correct.
                                if ((nextPosition.Position > estimatedShouldBeMin && nextPosition.Position < estimatedShouldBeMax)
                                    || (calculatedShouldBe != TimeSpan.Zero && nextPosition.Position > calculatedShouldBeMin && nextPosition.Position < calculatedShouldBeMax)
                                        || lastSentence)
                                {
                                    offset = nextPosition.Position - estimatedShouldBe;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // If we're at the last sentence the next position is calculated instead by using the previous position and adding a minute.
                            nextPosition = new AudioPosition()
                            {
                                Position = previousPosition.Position + previousPosition.Duration + TimeSpan.FromMinutes(1),
                                Duration = TimeSpan.Zero
                            };
                        }

                        // If next position is set to zero, calculate the position.
                        if (nextPosition.Position == TimeSpan.Zero)
                            nextPosition.Position = previousPosition.Position + TimeSpan.FromMinutes(5);

                        // The final step is to check if the position is not within the previous and next position.
                        if (closestAudio.Position < previousPosition.Position.Add(previousPosition.Duration) || closestAudio.Position > nextPosition.Position)
                        {
                            closestAudio.Position = TimeSpan.Zero;
                            closestAudio.Duration = TimeSpan.Zero;
                        }

                        this.currentChapter.Sentences[i].FirstAudioPosition = closestAudio;
                    }

                    //DebugWriteLine("After removal of incorrect values");

                    uint chapterSuccessCounter = 0;
                    // Count success of speech recognition, this is not 100% accurate because 
                    // the speech recognition can give false hits even if they seem right.
                    for (int i = 0; i < this.currentChapter.Sentences.Count; i++)
                        if (this.currentChapter.Sentences[i].FirstAudioPosition.Position != TimeSpan.Zero && this.currentChapter.Sentences[i].FirstAudioPosition.Duration != TimeSpan.Zero)
                            chapterSuccessCounter++;
                    this.successCounter += chapterSuccessCounter;
                    this.currentChapter.Successrate = (float)Math.Round((chapterSuccessCounter / (double)this.currentChapter.Sentences.Count) * 100);

                    // If first sentence is not set to a duration, estimate it. This will most of the time always get an incorrect value.
                    if (this.currentChapter.Sentences[0].FirstAudioPosition.Duration == TimeSpan.Zero)
                    {
                        this.currentChapter.Sentences[0].FirstAudioPosition.Duration = TimeSpan.FromSeconds(this.currentChapter.Sentences[0].CharCount / charsPerSecond);

                        if (this.currentChapter.Sentences[0].FirstAudioPosition.Position == TimeSpan.Zero && lastChapter != null)
                            this.currentChapter.Sentences[0].FirstAudioPosition.Position = TimeSpan.FromSeconds(EstimatePositionInSeconds(0, TimeSpan.Zero));

                        if (this.structure != AudioBookStructure.CHAPTER_STRUCTURE)
                        {
                            TimeSpan time = this.currentChapter.Sentences[0].FirstAudioPosition.Position;

                            int audioFileIndex = 0;
                            for (audioFileIndex = 0; audioFileIndex < this.mp3Files.Count; audioFileIndex++)
                            {
                                time -= this.mp3Files[audioFileIndex].TotalTime;

                                if (time < TimeSpan.Zero)
                                    break;
                            }

                            this.currentChapter.Sentences[0].FirstAudioPosition.AudioFileIndex = audioFileIndex;
                        }
                    }

                    // Estimate blanks
                    for (int i = 1; i < this.currentChapter.Sentences.Count; i++)
                    {
                        // If the sentence already has a position or if the previous sentence doesn't have a duration, continue to the next loop.
                        if (this.currentChapter.Sentences[i].FirstAudioPosition.Position != TimeSpan.Zero || this.currentChapter.Sentences[i - 1].FirstAudioPosition.Duration == TimeSpan.Zero)
                            continue;

                        // Set the position.
                        this.currentChapter.Sentences[i].FirstAudioPosition.Position = this.currentChapter.Sentences[i - 1].FirstAudioPosition.Position + this.currentChapter.Sentences[i - 1].FirstAudioPosition.Duration;

                        // If the next sentence has a position, use it to calculate the duration.
                        if ((i + 1) < this.currentChapter.Sentences.Count && this.currentChapter.Sentences[i + 1].FirstAudioPosition.Position != TimeSpan.Zero)
                        {
                            // Calculate the duration. The duration can't be less than zero.
                            TimeSpan duration = this.currentChapter.Sentences[i + 1].FirstAudioPosition.Position - this.currentChapter.Sentences[i].FirstAudioPosition.Position;
                            this.currentChapter.Sentences[i].FirstAudioPosition.Duration = (duration < TimeSpan.Zero) ? TimeSpan.Zero : duration;
                        }
                        else
                            this.currentChapter.Sentences[i].FirstAudioPosition.Duration = TimeSpan.FromSeconds(this.currentChapter.Sentences[i].CharCount / charsPerSecond);

                        // If the book uses a non chapter structure, calculate the audiofile index.
                        if (this.structure != AudioBookStructure.CHAPTER_STRUCTURE)
                        {
                            TimeSpan time = this.currentChapter.Sentences[i].FirstAudioPosition.Position;

                            int audioFileIndex = 0;
                            for (audioFileIndex = 0; audioFileIndex < this.mp3Files.Count; audioFileIndex++)
                            {
                                time -= this.mp3Files[audioFileIndex].TotalTime;

                                if (time < TimeSpan.Zero)
                                    break;
                            }

                            this.currentChapter.Sentences[i].FirstAudioPosition.AudioFileIndex = audioFileIndex;
                        }
                    }

                    //DebugWriteLine("After estimation");
                }

                resetEvent.Set();
            }
        }

        /// <summary>
        /// Estmimates the position of the sentence, in seconds.
        /// </summary>
        /// <param name="sentenceLine">The sentence to estimate the position for.</param>
        /// <param name="charsPerSeconds">The amount of chars per second.</param>
        /// <param name="offset">The offset from previous calculations.</param>
        /// <returns>The position of the sentence, in seconds.</returns>
        private double EstimatePositionInSeconds(int sentenceLine, TimeSpan offset)
        {
            double charCount = 0;
            for (int i = 0; i < sentenceLine; i++)
                charCount += this.currentChapter.Sentences[i].CharCount;

            if (lastChapter == null)
                return (charCount / this.charsPerSecond) + offset.TotalSeconds;
            else
            {
                if (this.structure == AudioBookStructure.CHAPTER_STRUCTURE && this.lastChapter.Sentences.Count > 0)
                {
                    double totalSeconds = 0;
                    for (int i = 0; i < this.currentChapter.Sentences[0].FirstAudioPosition.AudioFileIndex; i++)
                        totalSeconds += this.mp3Files[i].TotalTime.TotalSeconds;

                    return (charCount / this.charsPerSecond) + offset.TotalSeconds + totalSeconds;
                }
                else
                {
                    Sentence lastSentence = lastChapter.GetLastSentenceWithValues();
                    if (lastSentence == null)
                        return (charCount / this.charsPerSecond) + offset.TotalSeconds;
                    else
                        return (charCount / this.charsPerSecond) + offset.TotalSeconds + lastSentence.FirstAudioPosition.TotalSeconds();
                }
            }
        }

        /// <summary>
        /// Helper for writing debug messages.
        /// </summary>
        private void DebugWriteLine(string header)
        {
            System.Diagnostics.Debug.WriteLine("###################" + header + "#####################");

            System.Diagnostics.Debug.WriteLine("ID # Position ### Duration ### Text");

            for (int i = 0; i < this.currentChapter.Sentences.Count; i++)
            {
                System.Diagnostics.Debug.Write(i);
                System.Diagnostics.Debug.Write(" - ");
                System.Diagnostics.Debug.Write(this.currentChapter.Sentences[i].FirstAudioPosition.AudioFileIndex);
                System.Diagnostics.Debug.Write(" - ");
                System.Diagnostics.Debug.Write(this.currentChapter.Sentences[i].FirstAudioPosition.Position);
                System.Diagnostics.Debug.Write(" - ");
                System.Diagnostics.Debug.Write(this.currentChapter.Sentences[i].FirstAudioPosition.Duration);
                System.Diagnostics.Debug.Write(" - ");
                System.Diagnostics.Debug.WriteLine(this.currentChapter.Sentences[i].Text);
            }

            System.Diagnostics.Debug.WriteLine("########################################");
        }

        #endregion
    }
}