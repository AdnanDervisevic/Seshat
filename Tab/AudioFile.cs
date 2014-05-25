#region File Description
/*
 * AudioFile
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using NAudio.Wave;
using System.Collections.Generic;
#endregion

namespace Seshat.Tab
{
    /// <summary>
    /// Class representing an audio file.
    /// </summary>
    public sealed class AudioFile : Mp3FileReader
    {
        #region Properties

        /// <summary>
        /// Gets the filename.
        /// </summary>
        public string FileName { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new audio file instance.
        /// </summary>
        /// <param name="fileName">The audio file to load.</param>
        public AudioFile(string fileName)
            : base(fileName)
        {
            this.FileName = fileName;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Disposes the audio file.
        /// </summary>
        /// <param name="disposing"></param>
        public void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        #endregion
    }
}