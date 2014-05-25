#region File Description
/*
 * Sentence
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.Speech.Recognition;
using System.Collections.Generic;
using System.Text.RegularExpressions;
#endregion

namespace Seshat.Structure
{
    /// <summary>
    /// Class representing a sentence.
    /// </summary>
    public sealed class Sentence : IDisposable
    {
        #region Fields

        private string text = string.Empty;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the text.
        /// </summary>
        public string Text
        {
            get { return this.text; }
            private set
            {
                this.text = Regex.Replace(value.Trim(), @"\s+", " ");

                string text = this.text;

                text = Regex.Replace(text, "0", "nil");
                text = Regex.Replace(text, "1", "one");
                text = Regex.Replace(text, "2", "two");
                text = Regex.Replace(text, "3", "three");
                text = Regex.Replace(text, "4", "four");
                text = Regex.Replace(text, "5", "five");
                text = Regex.Replace(text, "6", "six");
                text = Regex.Replace(text, "7", "seven");
                text = Regex.Replace(text, "8", "eight");
                text = Regex.Replace(text, "9", "nine");

                this.CharCount = Regex.Matches(text, @"[\w0-9?!.]").Count;
            }
        }

        /// <summary>
        /// Gets or sets the original text.
        /// </summary>
        public string OriginalText { get; set; }

        /// <summary>
        /// Gets the first audio position.
        /// </summary>
        public AudioPosition FirstAudioPosition
        {
            get
            {
                if (AudioPositions.Count == 0)
                    this.AudioPositions.Add(new AudioPosition());

                return AudioPositions[0];
            }
            set
            {
                if (AudioPositions.Count == 0)
                    AudioPositions.Add(value);
                else
                    AudioPositions[0] = value;
            }
        }

        /// <summary>
        /// Gets or sets the list of audio positions.
        /// </summary>
        public List<AudioPosition> AudioPositions { get; set; }

        /// <summary>
        /// Gets or sets the grammar object.
        /// </summary>
        public Grammar Grammar { get; set; }

        /// <summary>
        /// The amount of words in the sentence.
        /// </summary>
        public int CharCount { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new sentence instance.
        /// </summary>
        /// <param name="text">The sentence.</param>
        public Sentence(string text)
        {
            this.AudioPositions = new List<AudioPosition>();
            this.CharCount = 0;
            this.Text = text;
        }

        /// <summary>
        /// Deconstructor
        /// </summary>
        ~Sentence()
        {
            this.Grammar = null;
            this.AudioPositions.Clear();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Disposes the sentence.
        /// </summary>
        public void Dispose()
        {
            this.text = string.Empty;
            this.OriginalText = string.Empty;
            this.AudioPositions.Clear();
            this.Grammar = null;
        }

        /// <summary>
        /// Get the closest audio position.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The audio position closest to the given position.</returns>
        public AudioPosition GetClosestAudioPosition(TimeSpan audioPosition)
        {
            for (int i = 0; i < AudioPositions.Count; i++)
            {
                if (this.AudioPositions[i].Position >= audioPosition)
                    return this.AudioPositions[i];
            }

            return new AudioPosition();
        }

        #endregion
    }
}