#region File Description
/*
 * BookTab
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.IO;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using mshtml;
using Seshat.Helpers;
using Seshat.Parsers;
using Seshat.Structure;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Linq;
#endregion

namespace Seshat.Tab
{
    public sealed class BookTab : TabItem, IDisposable
    {
        #region Fields

        public object xmlLock = new object();

        private CancellationTokenSource cancelToken;
        private TimingType timingType = TimingType.CORRUPT;
        private string file = string.Empty;
        private SpeechTimer speechTimer = null;
        private EstimatedTimer estimatedTimer = null;
        private AudioFile currentAudioFile = null;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the book.
        /// </summary>
        public Book Book { get; private set; }

        /// <summary>
        /// Gets or sets the current audio file.
        /// </summary>
        public AudioFile CurrentAudioFile
        {
            get 
            {
                return this.currentAudioFile;
            }
            private set
            {
                this.currentAudioFile = value;
                string xmlFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".xml";

                lock (xmlLock)
                {
                    if (File.Exists(xmlFile))
                    {    
                        XmlDocument doc = new XmlDocument();
                        doc.Load(xmlFile);

                        ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("CurrentAudioFileIndex", Convert.ToString(CurrentAudioFileIndex));

                        doc.Save(xmlFile);
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the current audio file index.
        /// </summary>
        public int CurrentAudioFileIndex { get; private set; }

        /// <summary>
        /// Gets or sets the volume.
        /// </summary>
        public float Volume { get; set; }

        /// <summary>
        /// Gets or sets whether we want to create a manual timing file.
        /// </summary>
        public bool CreateManual { get; set; }

        /// <summary>
        /// Gets the state of the manual timing.
        /// </summary>
        public State ManualState { get; private set; }

        /// <summary>
        /// Gets the manual timings.
        /// </summary>
        public Dictionary<int, List<AudioPosition>> ManualTimings { get; private set; }

        /// <summary>
        /// Gets the current chapter index when manually creating a timing file.
        /// </summary>
        public int CurrentChapterIndex { get; private set; }

        /// <summary>
        /// Gets the current sentence index when manually creating a timing file.
        /// </summary>
        public int CurrentSentenceIndex { get; private set; }

        /// <summary>
        /// Determines whether the book has loaded all the timings.
        /// </summary>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// Determines whether the book is loading something, can be audio etc.
        /// </summary>
        public bool IsAudioLoading { get; private set; }

        /// <summary>
        /// Gets the folder this tab is using to store temp files.
        /// </summary>
        public string TabFolder { get; private set; }

        /// <summary>
        /// Gets the browser.
        /// </summary>
        public WebBrowser Browser { get; private set; }

        /// <summary>
        /// Gets or sets the loading text, can be empty to show progress.
        /// </summary>
        public string LoadingText { get; set; }

        #endregion

        #region Events

        /// <summary>
        /// Event fired when the gui should update.
        /// </summary>
        public event EventHandler<double> UpdateProgress;

        /// <summary>
        /// Event fired when the tab is closed.
        /// </summary>
        public event EventHandler<FrameworkElement> Closed;

        /// <summary>
        /// Event fired when something is wrong.
        /// </summary>
        public event EventHandler<string> Exception;

        /// <summary>
        /// Event fired when the timings are loaded.
        /// </summary>
        public event EventHandler LoadingCompleted;

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new book tab instance.
        /// </summary>
        public BookTab(string file)
        {
            this.file = file;
            this.TabFolder = Guid.NewGuid().ToString();
            this.cancelToken = new CancellationTokenSource();

            Grid grid = new Grid();
            this.Content = grid;
            Browser = new WebBrowser();
            Browser.LoadCompleted += Browser_LoadCompleted;
            grid.Children.Add(Browser);

            ContextMenu contextMenu = new ContextMenu();
            MenuItem menuItem = new MenuItem();
            menuItem.Header = "Close tab";
            menuItem.Click += CloseTab_Clicked;
            contextMenu.Items.Add(menuItem);
            this.ContextMenu = contextMenu;

            this.speechTimer = new SpeechTimer(xmlLock);
            this.speechTimer.Completed += Timings_Loaded;
            this.speechTimer.Exception += ExceptionHandler;
            this.speechTimer.UpdateProgress += UpdateProgressHandler;
            this.speechTimer.Saving += estimatedTimer_Saving;

            this.estimatedTimer = new EstimatedTimer(xmlLock);
            this.estimatedTimer.Completed += Timings_Loaded;
            this.estimatedTimer.UpdateProgress += UpdateProgressHandler;
            this.estimatedTimer.Saving += estimatedTimer_Saving;

            this.ManualState = State.Disabled;
            this.CurrentChapterIndex = 0;
            this.CurrentSentenceIndex = 0;
            this.ManualTimings = new Dictionary<int, List<AudioPosition>>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the book.
        /// </summary>
        /// <returns></returns>
        public TimingType Initialize()
        {
            this.Book = EPubParser.Parse(this.file);

            // Change tooltip and header.
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                this.Header = this.Book.Title.Truncate(30);
                this.ToolTip = this.Book.Title;
            }));

            using (MD5 md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(this.file))
                {
                    this.Book.Checksum = BitConverter.ToString(md5.ComputeHash(stream));
                }
            }

            // Craete the tmp image folder if it doesn't exist.
            if (!Directory.Exists(this.TabFolder + "/images"))
                Directory.CreateDirectory(this.TabFolder + "/images");

            // Save all the images from the ebook to the image folder.
            for (int i = 0; i < Book.Images.Count; i++)
            {
                using (FileStream file = new FileStream(this.TabFolder + "/images/" + Book.Images[i].FileName, FileMode.Create, System.IO.FileAccess.Write))
                {
                    byte[] bytes = new byte[Book.Images[i].Content.Length];
                    Book.Images[i].Content.Read(bytes, 0, (int)Book.Images[i].Content.Length);
                    file.Write(bytes, 0, bytes.Length);
                    Book.Images[i].Content.Close();
                }
            }

            // Save the html file.
            File.WriteAllText(this.TabFolder + "/index.html", Book.Content);

            // Navigate to the newly created html file.
            string curDir = Directory.GetCurrentDirectory();
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                this.Browser.Navigate(new Uri(string.Format("file:///{0}/{1}/index.html", curDir, this.TabFolder)));
            }));

            timingType = this.speechTimer.LoadExisting(this.Book);

            if (timingType != TimingType.NORMAL)
            {
                for (int i = 0; i < this.Book.AudioFiles.Count; i++)
                    this.Book.AudioFiles[i].Dispose();
                this.Book.AudioFiles.Clear();

                if (timingType == TimingType.MISSING_AUDIO)
                {
                    string timingsFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".xml";
                    if (File.Exists(timingsFile))
                        MessageBox.Show("You must select the correct audiofiles.", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    #region TimingType is either Corrupt or progress

                    string progressFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".progress";
                    if (File.Exists(progressFile))
                    {
                        if (MessageBox.Show("A manual timing progress file exists.\nShould this file be deleted?", "File found", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            File.Delete(progressFile);
                        }
                        else
                        {
                            try
                            {
                                XmlDocument xmlDoc = new XmlDocument();
                                xmlDoc.Load(progressFile);

                                this.Book.Successrate = Convert.ToSingle(((XmlElement)xmlDoc.GetElementsByTagName("Book")[0]).GetAttribute("Successrate"));

                                XmlNodeList audioFilesList = xmlDoc.GetElementsByTagName("AudioFile");
                                foreach (XmlElement audioFileElement in audioFilesList)
                                {
                                    if (File.Exists(audioFileElement.GetAttribute("Path")))
                                        this.Book.AudioFiles.Add(new AudioFile(audioFileElement.GetAttribute("Path")));
                                }

                                if (this.Book.AudioFiles.Count == 0)
                                {
                                    MessageBox.Show("You must select the correct audiofiles.", "Notification", MessageBoxButton.OK, MessageBoxImage.Information);
                                    timingType = TimingType.MISSING_AUDIO;
                                }

                                XmlElement chaptersElement = (XmlElement)xmlDoc.GetElementsByTagName("Chapters").Item(0);
                                XmlNodeList chapterNodeList = chaptersElement.GetElementsByTagName("Chapter");
                                foreach (XmlElement chapterElement in chapterNodeList)
                                {
                                    this.CurrentChapterIndex++;
                                    this.ManualTimings.Add(CurrentChapterIndex, new List<AudioPosition>());

                                    XmlNodeList sentenceNodeList = chapterElement.GetElementsByTagName("Sentence");
                                    foreach (XmlElement sentenceElement in chapterElement)
                                    {
                                        CurrentSentenceIndex++;
                                        this.ManualTimings[CurrentChapterIndex].Add(new AudioPosition()
                                        {
                                            AudioFileIndex = Convert.ToInt32(sentenceElement.GetAttribute("AudioFileIndex")),
                                            Position = TimeSpan.Parse(sentenceElement.GetAttribute("AudioPosition")),
                                            Duration = TimeSpan.Parse(sentenceElement.GetAttribute("Duration"))
                                        });
                                    }

                                    this.CurrentSentenceIndex = 0;
                                }

                                if (timingType != TimingType.MISSING_AUDIO)
                                {
                                    timingType = TimingType.PROGRESS;

                                    TimeSpan savedAudioPosition = TimeSpan.Zero;
                                    int savedAudioFileIndex = 0;
                                    float savedVolume = 0;

                                    // Read the current audio file and position from the XML document.
                                    lock (xmlLock)
                                    {
                                        if (File.Exists(progressFile))
                                        {
                                            XmlDocument doc = new XmlDocument();
                                            doc.Load(progressFile);

                                            savedVolume = Convert.ToSingle(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("Volume"));
                                            savedAudioFileIndex = Convert.ToInt32(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioFileIndex"));
                                            savedAudioPosition = TimeSpan.Parse(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioPosition"));
                                        }
                                    }

                                    this.SelectAudioFile(savedAudioFileIndex);
                                    this.CurrentAudioFile.CurrentTime = savedAudioPosition;
                                    this.Volume = savedVolume;

                                    this.IsAudioLoading = false;
                                    this.LoadingText = string.Empty;
                                    if (this.LoadingCompleted != null)
                                        this.LoadingCompleted(this, EventArgs.Empty);
                                }
                            }
                            catch (Exception)
                            {
                                MessageBox.Show("The progress file is corrupt, it will be removed,\ncopy it if you want to save it before you click OK.", "ERROR!", MessageBoxButton.OK, MessageBoxImage.Error);
                                File.Delete(progressFile);
                            }
                        }
                    }

                    #endregion
                }
            }
            else
            {
                #region TimingType is set to normal

                string xmlFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".xml";

                TimeSpan savedAudioPosition = TimeSpan.Zero;
                int savedAudioFileIndex = 0;
                float savedVolume = 0;

                // Read the current audio file and position from the XML document.
                lock (xmlLock)
                {
                    if (File.Exists(xmlFile))
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(xmlFile);

                        savedVolume = Convert.ToSingle(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("Volume"));
                        savedAudioFileIndex = Convert.ToInt32(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioFileIndex"));
                        savedAudioPosition = TimeSpan.Parse(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioPosition"));
                    }
                }

                this.SelectAudioFile(savedAudioFileIndex);
                this.CurrentAudioFile.CurrentTime = savedAudioPosition;
                this.Volume = savedVolume;

                this.IsLoaded = true;

                this.LoadingText = string.Empty;
                if (this.LoadingCompleted != null)
                    this.LoadingCompleted(this, EventArgs.Empty);

                #endregion
            }

            return timingType;
        }

        /// <summary>
        /// Initializes the tab.
        /// </summary>
        public async void InitializeAudio(string[] audioFiles)
        {
            try
            {
                this.IsAudioLoading = true;

                for (int i = 0; i < this.Book.AudioFiles.Count; i++)
                    this.Book.AudioFiles[i].Dispose(true);

                this.Book.AudioFiles.Clear();
                foreach (string audioFile in audioFiles)
                    this.Book.AudioFiles.Add(new AudioFile(audioFile));

                string timingsFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".xml";
                string progressFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".progress";

                bool askToCreateNewTimingFile = false;
                List<string> audioHashes = new List<string>();
                if (timingType == TimingType.MISSING_AUDIO)
                {
                    if (File.Exists(timingsFile))
                    {
                        using (MD5 md5 = MD5.Create())
                        {
                            XmlDocument xmlDoc = new XmlDocument();
                            xmlDoc.Load(timingsFile);

                            XmlNodeList audioFilesList = xmlDoc.GetElementsByTagName("AudioFile");

                            if (audioFilesList.Count != this.Book.AudioFiles.Count)
                                askToCreateNewTimingFile = true;
                            else
                            {
                                this.LoadingText = "Loading audio files";
                                if (this.LoadingCompleted != null)
                                    this.LoadingCompleted(this, EventArgs.Empty);

                                await Task.Run(() => 
                                {
                                    int i = 0;
                                    foreach (XmlElement audioFileElement in audioFilesList)
                                    {
                                        string md5Hash = BitConverter.ToString(md5.ComputeHash(this.Book.AudioFiles[i]));
                                        audioHashes.Add(md5Hash);
                                        if (audioFileElement.GetAttribute("Checksum") != md5Hash)
                                        {
                                            audioHashes.Clear();
                                            askToCreateNewTimingFile = true;
                                            break;
                                        }
                                        i++;
                                    }
                                });

                                this.LoadingText = "Loading audio files";
                                if (this.LoadingCompleted != null)
                                    this.LoadingCompleted(this, EventArgs.Empty);
                            }
                        }
                    }
                    else if (File.Exists(progressFile))
                    {
                        using (MD5 md5 = MD5.Create())
                        {
                            XmlDocument xmlDoc = new XmlDocument();
                            xmlDoc.Load(progressFile);

                            XmlNodeList audioFilesList = xmlDoc.GetElementsByTagName("AudioFile");

                            if (audioFilesList.Count != this.Book.AudioFiles.Count)
                                askToCreateNewTimingFile = true;
                            else
                            {
                                this.LoadingText = "Loading audio files";
                                if (this.LoadingCompleted != null)
                                    this.LoadingCompleted(this, EventArgs.Empty);

                                await Task.Run(() =>
                                {
                                    int i = 0;
                                    foreach (XmlElement audioFileElement in audioFilesList)
                                    {
                                        string md5Hash = BitConverter.ToString(md5.ComputeHash(this.Book.AudioFiles[i]));
                                        audioHashes.Add(md5Hash);
                                        if (audioFileElement.GetAttribute("Checksum") != md5Hash)
                                        {
                                            audioHashes.Clear();
                                            askToCreateNewTimingFile = true;
                                            break;
                                        }
                                        i++;
                                    }
                                });

                                this.LoadingText = "Loading audio files";
                                if (this.LoadingCompleted != null)
                                    this.LoadingCompleted(this, EventArgs.Empty);
                            }
                        }
                    }
                }

                if (askToCreateNewTimingFile)
                {
                    if (MessageBox.Show("You've selected the wrong audio files.\n\nShould the timing file be deleted?", "Error", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes)
                    {
                        if (File.Exists(timingsFile))
                            File.Delete(timingsFile);
                        else if (File.Exists(progressFile))
                            File.Delete(progressFile);
                    }

                    foreach (AudioFile audioFile in this.Book.AudioFiles)
                        audioFile.Dispose(true);

                    this.Book.AudioFiles.Clear();
                    return;
                }
                else
                {
                    if (timingType == TimingType.MISSING_AUDIO)
                    {
                        if (File.Exists(timingsFile))
                        {
                            // Update audio path.
                            lock (xmlLock)
                            {
                                XmlDocument doc = new XmlDocument();
                                doc.Load(timingsFile);

                                XmlElement audioFilesElement = (XmlElement)doc.GetElementsByTagName("AudioFiles").Item(0);
                                audioFilesElement.RemoveAll();
                                for (int i = 0; i < this.Book.AudioFiles.Count; i++)
                                {
                                    XmlElement audioFileElement = doc.CreateElement("AudioFile");
                                    audioFileElement.SetAttribute("Path", this.Book.AudioFiles[i].FileName);
                                    audioFileElement.SetAttribute("Checksum", audioHashes[i]);

                                    audioFilesElement.AppendChild(audioFileElement);
                                }
                                doc.Save(timingsFile);
                            }

                            TimeSpan savedAudioPosition = TimeSpan.Zero;
                            int savedAudioFileIndex = 0;
                            float savedVolume = 0;

                            // Read the current audio file and position from the XML document.
                            lock (xmlLock)
                            {
                                if (File.Exists(timingsFile))
                                {
                                    XmlDocument doc = new XmlDocument();
                                    doc.Load(timingsFile);

                                    savedVolume = Convert.ToSingle(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("Volume"));
                                    savedAudioFileIndex = Convert.ToInt32(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioFileIndex"));
                                    savedAudioPosition = TimeSpan.Parse(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioPosition"));
                                }
                            }

                            this.SelectAudioFile(savedAudioFileIndex);
                            this.CurrentAudioFile.CurrentTime = savedAudioPosition;
                            this.Volume = savedVolume;

                            this.LoadingText = string.Empty;
                            this.IsLoaded = true;
                            if (this.LoadingCompleted != null)
                                this.LoadingCompleted(this, EventArgs.Empty);
                        }
                        else if (File.Exists(progressFile))
                        {
                            // Update audio path.
                            lock (xmlLock)
                            {
                                XmlDocument doc = new XmlDocument();
                                doc.Load(progressFile);

                                XmlElement audioFilesElement = (XmlElement)doc.GetElementsByTagName("AudioFiles").Item(0);
                                audioFilesElement.RemoveAll();
                                for (int i = 0; i < this.Book.AudioFiles.Count; i++)
                                {
                                    XmlElement audioFileElement = doc.CreateElement("AudioFile");
                                    audioFileElement.SetAttribute("Path", this.Book.AudioFiles[i].FileName);
                                    audioFileElement.SetAttribute("Checksum", audioHashes[i]);

                                    audioFilesElement.AppendChild(audioFileElement);
                                }
                                doc.Save(progressFile);
                            }

                            TimeSpan savedAudioPosition = TimeSpan.Zero;
                            int savedAudioFileIndex = 0;
                            float savedVolume = 0;

                            // Read the current audio file and position from the XML document.
                            lock (xmlLock)
                            {
                                if (File.Exists(progressFile))
                                {
                                    XmlDocument doc = new XmlDocument();
                                    doc.Load(progressFile);

                                    savedVolume = Convert.ToSingle(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("Volume"));
                                    savedAudioFileIndex = Convert.ToInt32(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioFileIndex"));
                                    savedAudioPosition = TimeSpan.Parse(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("CurrentAudioPosition"));
                                }
                            }

                            this.SelectAudioFile(savedAudioFileIndex);
                            this.CurrentAudioFile.CurrentTime = savedAudioPosition;
                            this.Volume = savedVolume;

                            this.IsAudioLoading = false;
                            this.LoadingText = string.Empty;
                            if (this.LoadingCompleted != null)
                                this.LoadingCompleted(this, EventArgs.Empty);
                        }
                    }
                }

                if (timingType == TimingType.CORRUPT)
                {
                    // Select the saved audio file or the first audio file.
                    this.SelectAudioFile(0);
                    this.CurrentAudioFile.CurrentTime = TimeSpan.Zero;
                    
                    this.IsAudioLoading = false;
                    this.LoadingText = string.Empty;
                    if (this.LoadingCompleted != null)
                        this.LoadingCompleted(this, EventArgs.Empty);
                }
            }
            catch (Exception ex) { }
        }

        /// <summary>
        /// Start creating a timing file.
        /// </summary>
        public void CreateSpeechTimingFile()
        {
            this.ManualTimings.Clear();
            this.ManualState = State.Disabled;
            this.CurrentChapterIndex = 0;
            this.CurrentSentenceIndex = 0;

            this.IsAudioLoading = true;
            System.Threading.ThreadPool.QueueUserWorkItem((object state) =>
            {
                this.speechTimer.Recognize(Book);
            });
        }

        /// <summary>
        /// Start estimating a timing file.
        /// </summary>
        public void CreateEstimateTimingFile()
        {
            this.ManualTimings.Clear();
            this.ManualState = State.Disabled;
            this.CurrentChapterIndex = 0;
            this.CurrentSentenceIndex = 0;

            this.IsAudioLoading = true;
            System.Threading.ThreadPool.QueueUserWorkItem((object state) =>
            {
                this.estimatedTimer.Estimate(Book);
            });
        }

        /// <summary>
        /// Saves the scroll value to the xml file.
        /// </summary>
        public void SaveScrollValue()
        {
            string xmlFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".xml";

            lock (xmlLock)
            {
                if (File.Exists(xmlFile))
                {
                    HTMLDocument htmlDoc = this.Browser.Document as HTMLDocument;
                    XmlDocument doc = new XmlDocument();

                    doc.Load(xmlFile);

                    ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("ScrollValue", Convert.ToString(htmlDoc.getElementsByTagName("HTML").item(0).ScrollTop));
                    ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("Volume", Convert.ToString(this.Volume));

                    doc.Save(xmlFile);
                }
            }
        }

        /// <summary>
        /// Determines whether this book tab has an audio file after the current one.
        /// </summary>
        /// <returns>Returns true if there is a next audio file.</returns>
        public bool HasNextAudioFile()
        {
            return (this.CurrentAudioFileIndex + 1 < this.Book.AudioFiles.Count);
        }

        /// <summary>
        /// Selects the specified audio file.
        /// </summary>
        /// <param name="audioFileIndex">The index of the audio file to select.</param>
        public void SelectAudioFile(int audioFileIndex)
        {
            if (audioFileIndex == this.CurrentAudioFileIndex && this.CurrentAudioFile != null)
                return;

            if (audioFileIndex >= 0 && audioFileIndex < this.Book.AudioFiles.Count)
            {
                this.CurrentAudioFileIndex = audioFileIndex;
                this.CurrentAudioFile = this.Book.AudioFiles[CurrentAudioFileIndex];
                this.CurrentAudioFile.Position = 0;
            }
            else
            {
                this.currentAudioFile = null;
            }
        }

        /// <summary>
        /// Disposes the book tab.
        /// </summary>
        public void Dispose()
        {
            this.estimatedTimer.Dispose();
            this.speechTimer.Dispose();
            this.Book.Dispose();

            if (Directory.Exists(this.TabFolder))
                Directory.Delete(this.TabFolder, true);
        }

        #endregion

        #region Manual Methods

        /// <summary>
        /// Adds a new chapter to the timing file.
        /// </summary>
        public void ManualAddChapter()
        {
            if (this.ManualState != State.Enabled)
            {
                if (this.ManualState == State.Saved)
                {
                    this.ManualTimings.Clear();
                    this.CurrentChapterIndex = 0;
                }

                this.ManualState = State.Enabled;
            }

            this.CurrentSentenceIndex = 0;
            this.CurrentChapterIndex++;
            this.ManualTimings.Add(CurrentChapterIndex, new List<AudioPosition>());
        }

        /// <summary>
        /// Adds a new sentence to the timing file.
        /// </summary>
        public TimeSpan ManualAddSentence(TimeSpan elapsed, int audioFileIndex)
        {
            if (this.ManualState == State.Enabled)
            {
                for (int i = 0; i < audioFileIndex; i++)
                    elapsed += this.Book.AudioFiles[i].TotalTime;

                //elapsed += savedTimeSpan;

                    /*
                    TimeSpan elapsed = this.stopwatch.Elapsed;
                    elapsed = elapsed.Add(savedTimeSpan);*/

                    this.ManualTimings[CurrentChapterIndex].Add(new AudioPosition()
                        {
                            AudioFileIndex = audioFileIndex,
                            Position = elapsed
                        });

                this.CurrentSentenceIndex++;
                return elapsed;
            }
            else
            {
                if (this.ManualState == State.Saved)
                {
                    this.CurrentChapterIndex++;
                    this.ManualTimings.Add(CurrentChapterIndex, new List<AudioPosition>());
                }

                this.ManualState = State.Enabled;

                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Saves or pause the timing file timer.
        /// </summary>
        public async Task ManualSave()
        {
            HTMLDocument htmlDoc = null;
            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                htmlDoc = this.Browser.Document as HTMLDocument;
            }));

            if (this.ManualState == State.Enabled)
            {
                this.ManualState = State.Disabled;
            }
            else
            {
                // Calculate Durations
                foreach (KeyValuePair<int, List<AudioPosition>> chapter in this.ManualTimings)
                    for (int i = 1; i < chapter.Value.Count; i++)
                        chapter.Value[i - 1].Duration = chapter.Value[i].Position - chapter.Value[i - 1].Position;

                // Save to XML
                string progressFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".progress";

                if (File.Exists(progressFile))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(progressFile);

                    List<AudioPosition> lastManualTimings = this.ManualTimings.Last().Value;

                    if (lastManualTimings.Count > 0)
                        ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("CompletedTime", Convert.ToString(lastManualTimings[lastManualTimings.Count - 1].Position + lastManualTimings[lastManualTimings.Count - 1].Duration));

                    ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("Volume", Convert.ToString(this.Volume));
                    ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("CurrentAudioFileIndex", Convert.ToString(this.CurrentAudioFileIndex));
                    ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("CurrentAudioPosition", Convert.ToString(this.CurrentAudioFile.CurrentTime));
                    ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("ScrollValue", Convert.ToString(htmlDoc.getElementsByTagName("HTML").item(0).ScrollTop));

                    XmlElement chaptersElement = (XmlElement)doc.GetElementsByTagName("Chapters").Item(0);
                    chaptersElement.RemoveAll();

                    foreach (KeyValuePair<int, List<AudioPosition>> manualTiming in this.ManualTimings)
                    {
                        XmlElement chapterElement = doc.CreateElement("Chapter");
                        chapterElement.SetAttribute("Successrate", "100");

                        for (int i = 0; i < manualTiming.Value.Count; i++)
                        {
                            if (manualTiming.Value[i].Duration == TimeSpan.Zero)
                                continue;

                            XmlElement sentenceElement = doc.CreateElement("Sentence");
                            sentenceElement.SetAttribute("AudioFileIndex", Convert.ToString(manualTiming.Value[i].AudioFileIndex));
                            sentenceElement.SetAttribute("AudioPosition", Convert.ToString(manualTiming.Value[i].Position));
                            sentenceElement.SetAttribute("Duration", Convert.ToString(manualTiming.Value[i].Duration));

                            chapterElement.AppendChild(sentenceElement);
                        }

                        chaptersElement.AppendChild(chapterElement);
                    }

                    doc.Save(progressFile);
                }
                else
                {
                    this.LoadingText = "Saving";
                    if (this.LoadingCompleted != null)
                        this.LoadingCompleted(this, EventArgs.Empty);

                    List<string> audioHashes = new List<string>();
                    await Task.Run(() =>
                    {
                        try
                        {
                            using (MD5 md5 = MD5.Create())
                            {
                                foreach (AudioFile audioFile in this.Book.AudioFiles)
                                {
                                    if (cancelToken.Token.IsCancellationRequested)
                                        cancelToken.Token.ThrowIfCancellationRequested();

                                    long previousPos = audioFile.Position;
                                    audioFile.Position = 0;
                                    audioHashes.Add(BitConverter.ToString(md5.ComputeHash(audioFile)));
                                    audioFile.Position = previousPos;
                                }
                            }
                        }
                        catch (Exception) { }
                    }, cancelToken.Token);

                    if (cancelToken.Token.IsCancellationRequested)
                        cancelToken.Token.ThrowIfCancellationRequested();

                    XmlDocument doc = new XmlDocument();
                    XmlDeclaration dec = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
                    doc.AppendChild(dec);

                    XmlElement root = doc.CreateElement("Books");
                    doc.AppendChild(root);

                    XmlElement bookElement = doc.CreateElement("Book");

                    List<AudioPosition> lastManualTimings = this.ManualTimings.Last().Value;
                    if (lastManualTimings.Count > 0)
                        bookElement.SetAttribute("CompletedTime", Convert.ToString(lastManualTimings[lastManualTimings.Count - 1].Position));
                    else
                        bookElement.SetAttribute("CompletedTime", "00:00:00");

                    bookElement.SetAttribute("Successrate", "100");
                    bookElement.SetAttribute("CharactersPerSecond", "0");
                    bookElement.SetAttribute("Volume", Convert.ToString(this.Volume));
                    bookElement.SetAttribute("CurrentAudioFileIndex", Convert.ToString(this.CurrentAudioFileIndex));
                    bookElement.SetAttribute("CurrentAudioPosition", Convert.ToString(this.CurrentAudioFile.CurrentTime));
                    bookElement.SetAttribute("ScrollValue", Convert.ToString(htmlDoc.getElementsByTagName("HTML").item(0).ScrollTop));
                    root.AppendChild(bookElement);

                    XmlElement audioFilesElement = doc.CreateElement("AudioFiles");
                    for (int i = 0; i < this.Book.AudioFiles.Count; i++)
                    {
                        XmlElement audioFileElement = doc.CreateElement("AudioFile");
                        audioFileElement.SetAttribute("Path", this.Book.AudioFiles[i].FileName);
                        audioFileElement.SetAttribute("Checksum", audioHashes[i]);

                        audioFilesElement.AppendChild(audioFileElement);
                    }
                    bookElement.AppendChild(audioFilesElement);

                    XmlElement chaptersElement = doc.CreateElement("Chapters");
                    foreach (KeyValuePair<int, List<AudioPosition>> manualTiming in this.ManualTimings)
                    {
                        XmlElement chapterElement = doc.CreateElement("Chapter");
                        chapterElement.SetAttribute("Successrate", "100");
                        for (int i = 0; i < manualTiming.Value.Count; i++)
                        {
                            if (manualTiming.Value[i].Duration == TimeSpan.Zero)
                                continue;

                            XmlElement sentenceElement = doc.CreateElement("Sentence");
                            sentenceElement.SetAttribute("AudioFileIndex", Convert.ToString(manualTiming.Value[i].AudioFileIndex));
                            sentenceElement.SetAttribute("AudioPosition", Convert.ToString(manualTiming.Value[i].Position));
                            sentenceElement.SetAttribute("Duration", Convert.ToString(manualTiming.Value[i].Duration));

                            chapterElement.AppendChild(sentenceElement);
                        }

                        chaptersElement.AppendChild(chapterElement);
                    }

                    bookElement.AppendChild(chaptersElement);
                    doc.Save(progressFile);

                    this.LoadingText = "Saving";
                    if (this.LoadingCompleted != null)
                        this.LoadingCompleted(this, EventArgs.Empty);
                }

                this.ManualState = State.Saved;
            }
        }

        /// <summary>
        /// Converts the progress file to a timing file.
        /// </summary>
        public void ConvertProgressToTiming()
        {
            string timingFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".xml";
            string progressFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".progress";

            if (File.Exists(timingFile))
                File.Delete(timingFile);

            if (!File.Exists(progressFile))
            {
                MessageBox.Show("Error, no progress file can be found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            else
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(progressFile);

                ((XmlElement)doc.GetElementsByTagName("Chapters").Item(0)).RemoveAttribute("CurrentElapsed");
                doc.Save(timingFile);

                File.Delete(progressFile);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Method fired when browser has loaded the document.
        /// </summary>
        private void Browser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Uri.Fragment))
            {
                string timingFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".xml";
                string progressFile = MainWindow.TimingsFolder + @"\" + this.Book.Checksum + ".progress";

                lock (xmlLock)
                {
                    if (File.Exists(timingFile))
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(timingFile);

                        HTMLDocument htmlDoc = this.Browser.Document as HTMLDocument;
                        htmlDoc.parentWindow.scrollBy(0, Convert.ToInt32(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("ScrollValue")));
                    }
                    else if (File.Exists(progressFile))
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(progressFile);

                        HTMLDocument htmlDoc = this.Browser.Document as HTMLDocument;
                        htmlDoc.parentWindow.scrollBy(0, Convert.ToInt32(((XmlElement)doc.GetElementsByTagName("Book").Item(0)).GetAttribute("ScrollValue")));
                    }
                }
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the Close tab button.
        /// </summary>
        private void CloseTab_Clicked(object sender, System.Windows.RoutedEventArgs e)
        {
            this.Dispose();

            if (this.Closed != null)
                this.Closed(this, sender as FrameworkElement);
        }

        /// <summary>
        /// Method fired when the book has loaded all the timings.
        /// </summary>
        private void Timings_Loaded(object sender, Book e)
        {
            this.SelectAudioFile(0);
            this.CurrentAudioFile.CurrentTime = TimeSpan.Zero;
            this.Volume = 0;

            this.IsLoaded = true;

            this.LoadingText = string.Empty;
            if (this.LoadingCompleted != null)
                this.LoadingCompleted(this, EventArgs.Empty);
        }

        /// <summary>
        /// Method fired when to update the progress gui.
        /// </summary>
        private void UpdateProgressHandler(object sender, double e)
        {
            if (this.UpdateProgress != null)
                this.UpdateProgress(this, e);
        }

        /// <summary>
        /// Method fired when the speech timer throws an exception.
        /// </summary>
        private void ExceptionHandler(object sender, string e)
        {
            this.IsAudioLoading = false;

            if (this.Exception != null)
                this.Exception(this, e);
        }

        /// <summary>
        /// Method fired when we're saving.
        /// </summary>
        void estimatedTimer_Saving(object sender, EventArgs e)
        {
            this.LoadingText = "Saving";
            if (this.LoadingCompleted != null)
                this.LoadingCompleted(this, EventArgs.Empty);
        }

        #endregion
    }
}