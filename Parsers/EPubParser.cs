#region File Description
/*
 * EPubParser
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.IO;
using System.Speech.Recognition;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BinaryAnalysis.UnidecodeSharp;
using eBdb.EpubReader;
using Seshat.Helpers;
using Seshat.Structure;
using Seshat.Tab;
#endregion

namespace Seshat.Parsers
{
    /// <summary>
    /// Class containing methods for parsing epub files.
    /// </summary>
    public static class EPubParser
    {
        #region Public Methods

        /// <summary>
        /// Parses a book.
        /// </summary>
        /// <param name="bookFile">The book to parse.</param>
        /// <returns>Returns the parsed book.</returns>
        public static Book Parse(string bookFile)
        {
            Book book = new Book();
            Epub file = new Epub(bookFile);

            book.Filename = bookFile;

            // Load Title & Author
            book.Title = (file.Title.Count > 0) ? file.Title[0] : "Adnan Dervisevic & Tobias Oskarsson";
            book.Author = (file.Creator.Count > 0) ? file.Creator[0] : "Unknown author";
            book.Images = file.GetImages();
            book.Content = file.GetContentAsHtml();

            string startValue = "<style type=\"text/css\">";
            string endValue = "</style>";

            int startPos = 0;
            do
            {
                startPos = book.Content.IndexOf(startValue, startPos, StringComparison.InvariantCultureIgnoreCase);

                if (startPos < 0)
                    break;

                int endPos = book.Content.IndexOf(endValue, startPos, StringComparison.InvariantCultureIgnoreCase);

                if (endPos > startPos)
                    book.Content = book.Content.Remove(startPos, endPos - startPos);

            } while (startPos > 0);

            startValue = "<sub ";
            endValue = ">";

            startPos = 0;
            do
            {
                startPos = book.Content.IndexOf(startValue, startPos, StringComparison.InvariantCultureIgnoreCase);

                if (startPos < 0)
                    break;

                int endPos = book.Content.IndexOf(endValue, startPos, StringComparison.InvariantCultureIgnoreCase);

                if (endPos > startPos)
                {
                    book.Content = book.Content.Remove(startPos, endPos - startPos + 1);
                    book.Content = book.Content.Insert(startPos, " ");
                }

            } while (startPos > 0);

            startValue = "</sub";
            endValue = ">";

            startPos = 0;
            do
            {
                startPos = book.Content.IndexOf(startValue, startPos, StringComparison.InvariantCultureIgnoreCase);

                if (startPos < 0)
                    break;

                int endPos = book.Content.IndexOf(endValue, startPos, StringComparison.InvariantCultureIgnoreCase);

                if (endPos > startPos)
                    book.Content = book.Content.Remove(startPos, endPos - startPos + 1);

            } while (startPos > 0);

            book.Chapters.AddRange(ParseNavPoint(book.Title, file.TOC));

            return book;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Helper for prasing navigation points.
        /// </summary>
        /// <param name="audioDir">The path to the book.</param>
        /// <param name="usedAudioFiles">The list of already used audio files.</param>
        /// <param name="navPoints">The navigation points to parse.</param>
        /// <returns>The list of sub chapters.</returns>
        private static List<Chapter> ParseNavPoint(string title, List<NavPoint> navPoints)
        {
            List<Chapter> chapters = new List<Chapter>();

            foreach (NavPoint navPoint in navPoints)
            {
                if (navPoint.ContentData == null && navPoint.Children.Count == 0)
                    continue;

                Chapter chapter = new Chapter();
                chapter.Title = navPoint.Title;

                #region Build Sentences objects.

                string content = string.Empty;
                if (navPoint.ContentData != null)
                    content = Regex.Replace(navPoint.ContentData.GetContentAsPlainText(), @"\t|\n|\r", "");

                if (!string.IsNullOrWhiteSpace(content))
                {
                    string[] abbWithDots = new string[] { "Mr. ", "Mrs. ", "Ms. ", "Sir. ", "A.A ", "A.A.S ", "A.B.D ", "A.F.A ", "B.A. ", "B.F.A ", "B.S. ", "M.A. ", "M.Ed. ", "M.S. ", "Dr. ", "Esq. ", "Prof. ", "Ph.D. ", "M.D. ", "J.D. " };
                    string[] abbWithoutDots = new string[] { "Mr ", "Mrs ", "Ms ", "Sir ", "AA ", "AAS ", "ABD ", "AFA ", "BA ", "BFA ", "BS ", "MA ", "MEd ", "MS ", "Dr ", "Esq ", "Prof ", "PhD ", "MD ", "JD " };

                    for (int i = 0; i < abbWithDots.Length; i++)
                        content = content.Replace(abbWithDots[i], abbWithoutDots[i]);
                    
                    string[] sentences = Regex.Split(content, @"(?<=[\.]\W)");

                    for (int i = 0; i < sentences.Length; i++)
                        for (int j = 0; j < abbWithDots.Length; j++)
                            sentences[i] = sentences[i].Replace(abbWithoutDots[j], abbWithDots[j]);

                    try
                    {
                        if (sentences.Length > 0)
                        {
                            sentences[0] = Regex.Replace(sentences[0], @"\s{2,}", " ");
                            string chapterTitle = Regex.Replace(chapter.Title, @"\s{2,}", " ");

                            int length = sentences[0].IndexOf(chapterTitle, StringComparison.InvariantCultureIgnoreCase);
                            if (length >= 0)
                                sentences[0] = sentences[0].Remove(length, chapterTitle.Length);
                            else
                            {
                                string[] titleParts = chapterTitle.Split(':');

                                if (titleParts.Length > 1)
                                {
                                    length = sentences[0].IndexOf(chapterTitle.Replace(":", ""), StringComparison.InvariantCultureIgnoreCase);
                                    if (length >= 0)
                                        sentences[0] = sentences[0].Remove(length, chapterTitle.Replace(":", "").Length);
                                }

                                if (length == -1)
                                {
                                    length = sentences[0].IndexOf(titleParts[titleParts.Length - 1], StringComparison.InvariantCultureIgnoreCase);
                                    if (length >= 0)
                                        sentences[0] = sentences[0].Remove(length, titleParts[titleParts.Length - 1].Length);
                                }
                            }
                        }
                    }
                    catch (Exception) { }

                    if (sentences.Length < 1024)
                    {
                        for (int i = 0; i < sentences.Length; i++)
                        {
                            sentences[i] = Regex.Replace(sentences[i], @"\s{2,}", " ");

                            sentences[i] = sentences[i].Trim();
                            string originalText = sentences[i];
                            sentences[i] = sentences[i].RemoveChars('“', '’', '‘', '”');

                            if (string.IsNullOrWhiteSpace(sentences[i]) || sentences[i].Length == 1)
                                continue;

                            Sentence sentence = new Sentence(sentences[i]);
                            sentence.OriginalText = originalText;

                            if (Regex.Matches(sentences[i], @"[a-zA-Z]").Count > 0)
                                if (sentences[i].Split(' ').Length > 4)
                                    sentence.Grammar = new Grammar(new GrammarBuilder(sentence.Text.Replace("\"", "")));

                            chapter.Sentences.Add(sentence);
                        }

                        if (chapter.Sentences.Count >= SpeechTimer.MiniumSentencesPerChapter)
                            chapters.Add(chapter);
                    }
                    else
                    {
                        int divideToChaptersCount = (int)Math.Ceiling(sentences.Length / 1024.0);
                        Chapter[] divideToChapters = new Chapter[divideToChaptersCount];
                        
                        int i = 0;
                        for (int j = 0; j < divideToChaptersCount; j++)
                        {
                            divideToChapters[j] = new Chapter();

                            for (int k = 0; k < 1024 && i < sentences.Length; k++, i++)
                            {
                                sentences[i] = Regex.Replace(sentences[i], @"\s{2,}", " ");

                                sentences[i] = sentences[i].Trim();
                                string originalText = sentences[i];
                                sentences[i] = sentences[i].RemoveChars('“', '’', '‘', '”');

                                if (string.IsNullOrWhiteSpace(sentences[i]) || sentences[i].Length == 1)
                                    continue;

                                Sentence sentence = new Sentence(sentences[i]);
                                sentence.OriginalText = originalText;

                                if (Regex.Matches(sentences[i], @"[a-zA-Z]").Count > 0)
                                    if (sentences[i].Split(' ').Length > 4)
                                        sentence.Grammar = new Grammar(new GrammarBuilder(sentence.Text));

                                divideToChapters[j].Sentences.Add(sentence);
                            }
                        }

                        divideToChapters[0].Title = chapter.Title;

                        for (i = 0; i < divideToChaptersCount; i++)
                            chapters.Add(divideToChapters[i]);
                    }
                }

                #endregion

                if (navPoint.Children.Count > 0)
                {
                    List<Chapter> childChapters = ParseNavPoint(title, navPoint.Children);
                    chapters.AddRange(childChapters);
                }
            }

            return chapters;
        }

        #endregion
    }
}