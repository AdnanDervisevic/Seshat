#region File Description
/*
 * AudioPosition
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
#endregion

namespace Seshat.Structure
{
    /// <summary>
    /// Class representing an audio position, contains a
    /// position and a duration.
    /// </summary>
    public sealed class AudioPosition
    {
        #region Properties

        /// <summary>
        /// Gets or sets the index of the audio file.
        /// </summary>
        public int AudioFileIndex { get; set; }

        /// <summary>
        /// Gets or sets the position.
        /// </summary>
        public TimeSpan Position { get; set; }

        /// <summary>
        /// Gets or sets the duration.
        /// </summary>
        public TimeSpan Duration { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new audio position instance.
        /// </summary>
        public AudioPosition()
        {
            this.AudioFileIndex = -1;
            this.Position = TimeSpan.Zero;
            this.Duration = TimeSpan.Zero;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the total seconds of position + duration.
        /// </summary>
        /// <returns>The total seconds.</returns>
        public double TotalSeconds()
        {
            return this.Position.TotalSeconds + this.Duration.TotalSeconds;
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return Position.GetHashCode() + Duration.GetHashCode();
        }

        /// <summary>
        /// Compares this audio position with an object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True if the objects fields matches this audio positions fields; otherwise false.</returns>
        public override bool Equals(object obj)
        {
            // If parameter is null return false.
            if (obj == null)
                return false;

            // If parameter cannot be cast to Point return false.
            if (obj is AudioPosition)
                return Equals(obj as AudioPosition);

            return false;
        }

        /// <summary>
        /// Compares this audio position with another.
        /// </summary>
        /// <param name="p">The audio position to compare with.</param>
        /// <returns>True if the fields matches; otherwise false.</returns>
        public bool Equals(AudioPosition p)
        {
            // If parameter is null return false:
            if (p == null)
                return false;

            // Return true if the fields match:
            return (Position == p.Position && Duration == p.Duration && AudioFileIndex == p.AudioFileIndex);
        }

        /// <summary>
        /// Compares two audio positions.
        /// </summary>
        /// <param name="a">Audio position A</param>
        /// <param name="b">Audio position B</param>
        /// <returns>True if the given variables are equals; otherwise false.</returns>
        public static bool operator ==(AudioPosition a, AudioPosition b)
        {
            // If both are null, or both are same instance, return true.
            if (System.Object.ReferenceEquals(a, b))
                return true;

            // If one is null, but not both, return false.
            if (((object)a == null) || ((object)b == null))
                return false;

            // Return true if the fields match:
            return (a.Position == b.Position) && (a.Duration == b.Duration) && (a.AudioFileIndex == b.AudioFileIndex);
        }

        /// <summary>
        /// Compares two audio positions.
        /// </summary>
        /// <param name="a">Audio position A</param>
        /// <param name="b">Audio position B</param>
        /// <returns>True if the given variables are not equals; otherwise false.</returns>
        public static bool operator !=(AudioPosition a, AudioPosition b)
        {
            return !(a == b);
        }

        /// <summary>
        /// Subtracts an audio position from another.
        /// </summary>
        /// <param name="a">The audio position to subtract.</param>
        /// <param name="b">The audio position to subtract from.</param>
        /// <returns>The subtract audio position.</returns>
        public static AudioPosition operator -(AudioPosition a, AudioPosition b)
        {
            AudioPosition value = new AudioPosition();
            value.AudioFileIndex = a.AudioFileIndex - b.AudioFileIndex;
            value.Position = a.Position - b.Position;
            value.Duration = a.Duration - b.Duration;
            return value;
        }

        #endregion
    }
}