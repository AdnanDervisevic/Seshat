#region File Description
/*
 * CompareWindow
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.Xml;
using System.Windows;
using Microsoft.Win32;
using Seshat.Structure;
using Seshat.Structure.Differences;
using System.IO;
#endregion

namespace Seshat.xaml
{
    /// <summary>
    /// Interaction logic for CompareWindow.xaml
    /// </summary>
    public partial class CompareWindow : Window
    {
        #region Fields

        private BookDiff difference = null;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new compare window.
        /// </summary>
        public CompareWindow()
        {
            InitializeComponent();
        }

        #endregion

        #region Control Methods

        /// <summary>
        /// Method fired when the user clicks on compare
        /// </summary>
        private void Compare_Click(object sender, RoutedEventArgs e)
        {
            BookDiff baseFile = ParseBook(Convert.ToString(this.baseFile.ToolTip));
            BookDiff timingFile = ParseBook(Convert.ToString(this.timingFile.ToolTip));

            this.difference = CompareBooks(baseFile, timingFile);

            this.statusTxt.Text = "save differences";
            this.saveBtn.IsEnabled = true;
        }

        /// <summary>
        /// Method fired when the user clicks on save
        /// </summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFile = new SaveFileDialog();
            saveFile.Title = "Save differences";
            saveFile.Filter = "Difference File|*.difference";

            Nullable<bool> file = saveFile.ShowDialog(this);

            if (file.HasValue && file.Value)
            {
                if (File.Exists(saveFile.FileName))
                    File.Delete(saveFile.FileName);

                using (StreamWriter writer = File.AppendText(saveFile.FileName))
                {
                    writer.Write("Average: ");
                    writer.WriteLine(Math.Round(this.difference.Average.TotalSeconds, 3));
                    writer.Write("Median: ");
                    writer.WriteLine(Math.Round(this.difference.Median.TotalSeconds, 3));
                    writer.WriteLine();
                    for (int i = 0; i < difference.Chapters.Count; i++)
                    {
                        writer.Write("Chapter ");
                        writer.Write(i+1);
                        writer.Write(" Average: ");
                        writer.Write(Math.Round(difference.Chapters[i].Average.TotalSeconds, 3));
                        writer.Write(" Median: ");
                        writer.WriteLine(Math.Round(difference.Chapters[i].Median.TotalSeconds, 3));
                    }

                    writer.WriteLine();
                    writer.WriteLine("Book");
                    writer.WriteLine("----------------");

                    int c = 1;
                    foreach (ChapterDiff chapter in difference.Chapters)
                    {
                        writer.WriteLine("CHAPTER: " + c);
                        writer.WriteLine("------------------------------------");

                        foreach (AudioPosition audioPosition in chapter.Sentences)
                        {
                            writer.WriteLine(Math.Round(audioPosition.Position.TotalSeconds, 3));
                        }
                        c++;
                    }

                    writer.Flush();
                }

                this.statusTxt.Text = "differences saved";
            }
        }

        /// <summary>
        /// Method fired when the user clicks on open manual timing file.
        /// </summary>
        private void OpenBaseFile_Click(object sender, RoutedEventArgs e)
        {
            string file = GetFile("Select a base timing file");
            if (!string.IsNullOrEmpty(file))
            {
                this.baseFile.ToolTip = file;
                this.baseFile.Text = System.IO.Path.GetFileName(file);
                EnableCompare();
            }
        }

        /// <summary>
        /// Method fired when the user clicks on open speech estimated timing file.
        /// </summary>
        private void OpenTimingFile_Click(object sender, RoutedEventArgs e)
        {
            string file = GetFile("Select a timing file");
            if (!string.IsNullOrEmpty(file))
            {
                this.timingFile.ToolTip = file;
                this.timingFile.Text = System.IO.Path.GetFileName(file);
                EnableCompare();
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Helper for comparing two books.
        /// </summary>
        /// <param name="a">The baseline book.</param>
        /// <param name="b">The book to compare.</param>
        /// <returns>The differences.</returns>
        private BookDiff CompareBooks(BookDiff a, BookDiff b)
        {
            BookDiff value = new BookDiff();

            value.CompletedTime = a.CompletedTime - b.CompletedTime;

            for (int i = 0; i < a.Chapters.Count; i++)
            {
                value.Chapters.Add(new ChapterDiff());

                for (int j = 0; j < a.Chapters[i].Sentences.Count; j++)
                {
                    value.Chapters[i].Sentences.Add(new AudioPosition());
                    value.Chapters[i].Sentences[j] =
                        b.Chapters[i].Sentences[j] - a.Chapters[i].Sentences[j];
                }
            }

            return value;
        }

        /// <summary>
        /// Helper for parsing the book.
        /// </summary>
        /// <param name="file">The file to parse.</param>
        /// <returns>The parsed book.</returns>
        private BookDiff ParseBook(string file)
        {
            BookDiff book = new BookDiff();

            XmlDocument doc = new XmlDocument();
            doc.Load(file);

            book.CompletedTime = TimeSpan.Parse(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CompletedTime"));

            XmlNodeList chapters = doc.GetElementsByTagName("Chapter");
            int chapterIndex = 0;
            foreach (XmlElement chapter in chapters)
            {
                book.Chapters.Add(new ChapterDiff());

                int sentenceIndex = 0;
                XmlNodeList sentences = chapter.GetElementsByTagName("Sentence");
                foreach (XmlElement sentence in sentences)
                {
                    book.Chapters[chapterIndex].Sentences.Add(new AudioPosition());
                    book.Chapters[chapterIndex].Sentences[sentenceIndex] = new AudioPosition();
                    
                    book.Chapters[chapterIndex].Sentences[sentenceIndex].Position = TimeSpan.Parse(sentence.GetAttribute("AudioPosition"));
                    book.Chapters[chapterIndex].Sentences[sentenceIndex].Duration = TimeSpan.Parse(sentence.GetAttribute("Duration"));

                    /*
                    book.Chapters[chapterIndex].Sentences[sentenceIndex] = new AudioPosition()
                    {
                        Position = TimeSpan.Parse(sentence.GetAttribute("AudioPosition")),
                        Duration = TimeSpan.Parse(sentence.GetAttribute("Duration"))
                    };*/

                    sentenceIndex++;
                }

                chapterIndex++;
            }

            return book;
        }

        /// <summary>
        /// Helper for enabling the compare button.
        /// </summary>
        private void EnableCompare()
        {
            if (this.baseFile.Text != "..." && this.timingFile.Text != "...")
            {
                this.compareBtn.IsEnabled = true;
                this.statusTxt.Text = "files selected";
            }
        }

        /// <summary>
        /// Helper for opening a file.
        /// </summary>
        /// <param name="title">The title to show.</param>
        /// <returns>Either the opened file or three dots.</returns>
        private string GetFile(string title)
        {
            OpenFileDialog bookFile = new OpenFileDialog();
            bookFile.Title = title;
            bookFile.Filter = "Timing File|*.xml";

            Nullable<bool> result = bookFile.ShowDialog(this);

            if (result.HasValue)
                return bookFile.FileName;

            return null;
        }

        #endregion
    }
}