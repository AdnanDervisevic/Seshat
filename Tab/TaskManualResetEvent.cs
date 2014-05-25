#region File Description
/*
 * TaskManualResetEvent
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace Seshat.Tab
{
    public sealed class TaskManualResetEvent : IDisposable
    {
        #region Fields

        private CancellationTokenSource tokenSource;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the TaskManualResetEvent class with 
        /// a Boolean value indicating whether to set the initial state to signaled.
        /// </summary>
        /// <param name="initialState">True to set the initial state signaled; false to set the initial state to nonsignaled.</param>
        public TaskManualResetEvent(bool initialState)
        {
            tokenSource = new CancellationTokenSource();

            if (initialState)
                tokenSource.Cancel();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets the state of the event to signaled, allowing one or more waiting async methods to proceed.
        /// </summary>
        public void Set()
        {
            tokenSource.Cancel();
        }

        /// <summary>
        /// Sets the state of the event to nonsignaled, causing async methods to block.
        /// </summary>
        public void Reset()
        {
            tokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Blocks the current async method until the current WaitHandle receives a signal.
        /// </summary>
        /// <returns></returns>
        public async Task<bool> WaitOne()
        {
            if (tokenSource.Token.IsCancellationRequested)
                return true;

            try
            {
                await Task.Delay(-1, tokenSource.Token);
            }
            catch (Exception) { }

            return true;
        }

        /// <summary>
        /// Blocks the current async method until the current WaitHandle receives a signal, using a 32-bit signed integer to specify the time interval.
        /// </summary>
        /// <param name="millisecondsTimeout"></param>
        /// <returns></returns>
        public async Task<bool> WaitOne(int millisecondsTimeout)
        {
            if (tokenSource.Token.IsCancellationRequested)
                return true;

            try
            {
                await Task.Delay(millisecondsTimeout, tokenSource.Token);
            }
            catch (Exception) { }

            return true;
        }

        /// <summary>
        /// Blocks the current async method until the current instance receives a signal, using a TimeSpan to specify the time interval.
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public async Task<bool> WaitOne(TimeSpan timeout)
        {
            if (tokenSource.Token.IsCancellationRequested)
                return true;

            try
            {
                await Task.Delay(timeout, tokenSource.Token);
            }
            catch (Exception) { }

            return true;
        }

        /// <summary>
        /// Disposes the object.
        /// </summary>
        public void Dispose()
        {
            this.tokenSource.Dispose();
        }

        #endregion
    }
}