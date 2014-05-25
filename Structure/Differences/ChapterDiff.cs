#region File Description
/*
 * ChapterDiff
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.Linq;
using System.Collections.Generic;
#endregion

namespace Seshat.Structure.Differences
{
    public sealed class ChapterDiff
    {
        #region Properties

        /// <summary>
        /// Gets or sets the sentences.
        /// </summary>
        public List<AudioPosition> Sentences { get; set; }

        /// <summary>
        /// Gets or sets the average.
        /// </summary>
        public TimeSpan Average
        {
            get
            {
                List<TimeSpan> positions = new List<TimeSpan>();
                
                for (int i = 0; i < Sentences.Count; i++)
                    positions.Add(Sentences[i].Position);

                return new TimeSpan(Convert.ToInt64(positions.Average(obj => obj.Ticks)));
            }
        }

        /// <summary>
        /// Gets or sets the median.
        /// </summary>
        public TimeSpan Median
        {
            get
            {
                List<TimeSpan> positions = new List<TimeSpan>();

                for (int i = 0; i < Sentences.Count; i++)
                    positions.Add(Sentences[i].Position);

                positions.Sort();

                if (positions.Count % 2 == 0)
                    return positions[positions.Count / 2];
                else
                {
                    List<TimeSpan> median = new List<TimeSpan>();

                    int x = positions.Count / 2;
                    for (int i = x; i < x + 2; i++)
                        median.Add(positions[i]);

                    return new TimeSpan(Convert.ToInt64(median.Average(obj => obj.Ticks)));
                }
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new chapter difference.
        /// </summary>
        public ChapterDiff()
        {
            this.Sentences = new List<AudioPosition>();
        }

        #endregion
    }
}