#region File Description
/*
 * SpeechStream
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System.IO;
using System.Threading;
using System.Collections.Generic;
#endregion

namespace Seshat.Tab
{
    public sealed class SpeechStream : Stream
    {
        #region Fields

        private AutoResetEvent writeEvent;
        private AutoResetEvent readEvent;
        private List<byte> buffer;
        private int bufferSize;
        private int readPosition;
        private int writePosition;
        private bool reset;

        #endregion

        #region Properties

        /// <summary>
        /// Indicates whether we've reach the end of the file.
        /// </summary>
        public bool EndOfFile { get; set; }

        /// <summary>
        /// Indicates whether the current stream supports reading.
        /// </summary>
        public override bool CanRead
        {
            get { return true; }
        }

        /// <summary>
        /// Indicates whether the current stream supports writing.
        /// </summary>
        public override bool CanWrite
        {
            get { return true; }
        }

        /// <summary>
        /// Indicates whether the current stream supports seeking.
        /// </summary>
        public override bool CanSeek
        {
            get { return false; }
        }

        /// <summary>
        /// Gets the length in bytes of the stream.
        /// </summary>
        public override long Length
        {
            get { return -1L; }
        }

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        public override long Position
        {
            get { return 0L; }
            set { }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new speech stream instance with the given buffersize.
        /// </summary>
        /// <param name="bufferSize">The size of the buffer.</param>
        public SpeechStream(int bufferSize)
        {
            this.writeEvent = new AutoResetEvent(false);
            this.readEvent = new AutoResetEvent(false);
            this.bufferSize = bufferSize;
            this.buffer = new List<byte>(bufferSize);

            for (int i = 0; i < this.bufferSize; i++)
                this.buffer.Add(new byte());

            this.readPosition = 0;
            this.writePosition = 0;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets the position within the current stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter.</param>
        /// <param name="origin">A value of type System.IO.SeekOrigin indicating the reference point used to obtain the new position.</param>
        /// <returns>The new position within the current stream.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            return 0L;
        }

        /// <summary>
        /// Sets the length of the current stream.
        /// </summary>
        /// <param name="value">The desired length of the current stream in bytes.</param>
        public override void SetLength(long value) { }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
        /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
        /// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int i = 0;

            while (i < count && this.writeEvent != null)
            {
                if (!this.reset && this.readPosition >= this.writePosition)
                {
                    this.writeEvent.WaitOne(100, true);

                    if (this.EndOfFile)
                        return 0;

                    continue;
                }

                buffer[i] = this.buffer[this.readPosition + offset];
                this.buffer[this.readPosition + offset] = 0;
                this.readPosition++;

                if (this.readPosition == this.bufferSize)
                {
                    this.readPosition = 0;
                    this.reset = false;
                }

                i++;
            }

            return count;
        }

        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
        /// </summary>
        /// <param name="buffer">An array of bytes. This method copies count bytes from buffer to the current stream.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            int i = 0;
            while (i < offset + count && this.readEvent != null)
            {
                if (this.buffer[this.writePosition] != 0)
                {
                    this.readEvent.WaitOne(100, true);
                    continue;
                }

                this.buffer[this.writePosition] = buffer[i];
                this.writePosition++;

                if (this.writePosition == this.bufferSize)
                {
                    this.writePosition = 0;
                    this.reset = true;
                }

                i++;
            }

            this.writeEvent.Set();
        }

        /// <summary>
        /// Closes the current stream and releases any resources (such as sockets and file handles) associated with the current stream. 
        /// </summary>
        public override void Close()
        {
            this.readEvent.Close();
            this.readEvent = null;
            this.writeEvent.Close();
            this.writeEvent = null;
            base.Close();
        }

        /// <summary>
        /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device.
        /// </summary>
        public override void Flush() { }

        #endregion
    }
}