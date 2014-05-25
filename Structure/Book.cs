#region File Description
/*
 * Book
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System.Collections.Generic;
using eBdb.EpubReader;
using Seshat.Tab;
using System;
#endregion

namespace Seshat.Structure
{
    /// <summary>
    /// Class representing a book, containing audio files and chapters etc.
    /// </summary>
    public sealed class Book : IDisposable
    {
        #region Fields

        private double sentencesWithAudioFiles = -1;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the filename.
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// Gets or sets the title of this book.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the author of this book.
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// Gets or sets the checksum for this book.
        /// </summary>
        public string Checksum { get; set; }

        /// <summary>
        /// Gets or sets the list of audio files.
        /// </summary>
        public List<AudioFile> AudioFiles { get; set; }

        /// <summary>
        /// Gets or sets the list of parts.
        /// </summary>
        public List<Chapter> Chapters { get; private set; }

        /// <summary>
        /// Gets or sets the images inside this book.
        /// </summary>
        public List<EpubImage> Images { get; set; }

        /// <summary>
        /// Gets or sets the content.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Gets or sets the successrate of the speech recognition.
        /// </summary>
        public float Successrate { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new book instance.
        /// </summary>
        public Book()
        {
            this.AudioFiles = new List<AudioFile>();
            this.Chapters = new List<Chapter>();
            this.Images = new List<EpubImage>();
            this.Successrate = 0;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Disposes the book.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < this.AudioFiles.Count; i++)
                this.AudioFiles[i].Dispose(true);

            for (int i = 0; i < this.Chapters.Count; i++)
                this.Chapters[i].Dispose();

            this.Chapters.Clear();
        }

        /// <summary>
        /// Returns the amount of sentences with audio files.
        /// </summary>
        /// <returns>The amount of sentences with audio files.</returns>
        public double GetAmountOfSentencesWithAudioFiles()
        {
            if (this.sentencesWithAudioFiles >= 0)
                return this.sentencesWithAudioFiles;

            this.sentencesWithAudioFiles = 0;
            foreach (Chapter chapter in this.Chapters)
            {
                if (chapter.Sentences.Count < SpeechTimer.MiniumSentencesPerChapter)
                    continue;

                foreach (Sentence sentence in chapter.Sentences)
                {
                    if (sentence.FirstAudioPosition.AudioFileIndex >= -1)
                        this.sentencesWithAudioFiles++;
                }
            }

            return this.sentencesWithAudioFiles;
        }

        #endregion
    }
}