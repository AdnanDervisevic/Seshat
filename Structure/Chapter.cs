#region File Description
/*
 * Chapter
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.Collections.Generic;
#endregion

namespace Seshat.Structure
{
    /// <summary>
    /// Class representing a chapter, contains a
    /// title and a list of sentences.
    /// </summary>
    public sealed class Chapter : IDisposable
    {
        #region Properties

        /// <summary>
        /// Gets or sets the chapter title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the list of pages.
        /// </summary>
        public List<Sentence> Sentences { get; set; }

        /// <summary>
        /// Gets the amount of words in this page.
        /// </summary>
        public long CharCount
        {
            get
            {
                long count = 0;

                for (int i = 0; i < this.Sentences.Count; i++)
                    count += this.Sentences[i].CharCount;

                return count;
            }
        }

        /// <summary>
        /// Gets or sets the successrate of the speech recognition.
        /// </summary>
        public float Successrate { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new chapter instance.
        /// </summary>
        public Chapter()
        {
            this.Title = string.Empty;
            this.Sentences = new List<Sentence>();
            this.Successrate = 0;
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~Chapter()
        {
            this.Sentences.Clear();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Disposes the chapter.
        /// </summary>
        public void Dispose()
        {
            this.Title = string.Empty;
            for (int i = 0; i < Sentences.Count; i++)
                this.Sentences[i].Dispose();

            this.Sentences.Clear();
        }

        /// <summary>
        /// Returns the last sentence with a time value.
        /// </summary>
        /// <returns>The last sentence with a time value.</returns>
        public Sentence GetLastSentenceWithValues()
        {
            for (int i = Sentences.Count - 1; i >= 0; i--)
                if (Sentences[i].FirstAudioPosition.Position > TimeSpan.Zero)
                    return this.Sentences[i];

            return null;
        }

        #endregion
    }
}