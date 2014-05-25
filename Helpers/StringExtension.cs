#region File Description
/*
 * StringExtension
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

namespace Seshat.Helpers
{
    /// <summary>
    /// Class containing extensions methods for the string struct.
    /// </summary>
    public static class StringExtensions
    {
        #region Public Methods

        /// <summary>
        /// Removes specified char values from a string.
        /// </summary>
        /// <param name="value">The string to remove from.</param>
        /// <param name="chars">The char values to remove.</param>
        /// <returns>The string without the specified char values.</returns>
        public static string RemoveChars(this string value, params char[] chars)
        {
            int idx = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                do
                {
                    idx = value.IndexOf(chars[i]);

                    if (idx >= 0)
                        value = value.Remove(idx, 1);

                } while (idx >= 0);
            }

            return value;
        }

        /// <summary>
        /// Truncates the specified string and adds three dots.
        /// </summary>
        /// <param name="value">The string to truncate.</param>
        /// <param name="maxChars">The max amount of characters.</param>
        /// <returns>The truncated string.</returns>
        public static string Truncate(this string value, int maxChars)
        {
            return value.Length <= maxChars ? value : value.Substring(0, maxChars) + "...";
        }

        #endregion
    }
}