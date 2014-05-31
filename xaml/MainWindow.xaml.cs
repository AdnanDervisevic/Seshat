#region File Description
/*
 * MainWindow
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System;
using System.IO;
using System.Xml;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using mshtml;
using Microsoft.Win32;
using NAudio.Wave;
using Seshat.Tab;
using Seshat.xaml;
using Seshat.Helpers;
using Seshat.Structure;
using System.Text;
#endregion

namespace Seshat
{
    /// <summary>
    /// The different states for the manual tool.
    /// </summary>
    public enum State
    {
        Disabled,
        Enabled,
        Saved
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        #region Consts

        /// <summary>
        /// The timings folder.
        /// </summary>
        public const string TimingsFolder = "Timings";

        #endregion

        #region Fields

        private bool isMouseSlider = true;
        private BookTab currentTab = null;
        private Timer scrollTimer = null;
        private Timer trackbarTimer = null;
        private IWavePlayer audioPlayer = null;

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs a new main window instance.
        /// </summary>
        public MainWindow()
        {
            // Sets the culture.
            CultureInfo cultureInfo = new CultureInfo("en-GB");
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

            // Removes all the previous temp folders if they exists.
            DirectoryInfo info = new DirectoryInfo(Directory.GetCurrentDirectory());
            Regex regex = new Regex(@"^(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}$");
            foreach (DirectoryInfo dir in info.GetDirectories())
            {
                if (regex.IsMatch(dir.Name))
                    dir.Delete(true);
            }

            InitializeComponent();

            if (!Directory.Exists(MainWindow.TimingsFolder))
                Directory.CreateDirectory(MainWindow.TimingsFolder);

            this.audioPlayer = new WaveOut();

            this.trackbarTimer = new Timer();
            this.trackbarTimer.Elapsed += trackbarTimer_Elapsed;
            this.trackbarTimer.Interval = 1000;

            this.scrollTimer = new Timer();
            this.scrollTimer.Elapsed += scrollTimer_Elapsed;
            this.scrollTimer.Interval = 1800;
            this.scrollTimer.Start();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Disposes the mainwindow.
        /// </summary>
        public void Dispose()
        {
            this.scrollTimer.Dispose();
            this.trackbarTimer.Dispose();

            for (int i = 0; i < this.tabControl.Items.Count; i++)
            {
                BookTab tab = (BookTab)this.tabControl.Items[i];
                tab.Dispose();
            }

            this.audioPlayer.Dispose();
        }

        #endregion

        #region Manual Events

        /// <summary>
        /// Method fired when the user clicks on the chapter button.
        /// </summary>
        private void addChapter(object sender, ExecutedRoutedEventArgs e)
        {
            if (!this.chapterBtn.IsEnabled)
                return;

            if (this.currentTab != null)
            {
                if (this.currentTab.ManualState != State.Enabled)
                {
                    if (this.currentTab.ManualState == State.Saved)
                        listBox.Items.Clear();

                    chapterBtn.Content = "Chapter";
                    sentenceBtn.Content = "Sentence";
                    saveBtb.Content = "Pause";
                    saveBtb.IsEnabled = true;
                    sentenceBtn.IsEnabled = true;

                    this.currentTab.ManualAddChapter();

                    Play_Clicked(this, null);

                    listBox.Items.Add("Chapter " + this.currentTab.CurrentChapterIndex);
                    listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                }
                else
                {
                    this.currentTab.ManualAddChapter();

                    listBox.Items.Add("Chapter " + this.currentTab.CurrentChapterIndex);
                    listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                }
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the sentence button.
        /// </summary>
        private void addSentence(object sender, ExecutedRoutedEventArgs e)
        {
            if (!this.sentenceBtn.IsEnabled)
                return;

            if (this.currentTab != null)
            {
                if (this.currentTab.ManualState == State.Enabled)
                {
                    TimeSpan elapsed = this.currentTab.ManualAddSentence(this.currentTab.CurrentAudioFile.CurrentTime, this.currentTab.CurrentAudioFileIndex);
                    
                    listBox.Items.Add("Sentence " + this.currentTab.CurrentSentenceIndex + ": " + Convert.ToString(elapsed));
                    listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                }
                else
                {
                    chapterBtn.Content = "Chapter";
                    sentenceBtn.Content = "Sentence";
                    saveBtb.Content = "Pause";
                    chapterBtn.IsEnabled = true;
                    saveBtb.IsEnabled = true;

                    State previousState = this.currentTab.ManualState;
                    this.currentTab.ManualAddSentence(this.currentTab.CurrentAudioFile.CurrentTime, this.currentTab.CurrentAudioFileIndex);
                    Play_Clicked(this, null);

                    if (previousState == State.Saved)
                    {
                        listBox.Items.Add("Chapter " + this.currentTab.CurrentChapterIndex);
                        listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                    }
                }
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the save button.
        /// </summary>
        private async void saveClicked(object sender, ExecutedRoutedEventArgs e)
        {
            if (!this.saveBtb.IsEnabled)
                return;

            if (this.currentTab != null)
            {
                sentenceBtn.Content = "Continue";

                if (this.currentTab.ManualState == State.Enabled)
                {
                    chapterBtn.IsEnabled = false;

                    saveBtb.Content = "Save";
                    Play_Clicked(this, null);
                    await this.currentTab.ManualSave();
                }
                else
                {
                    chapterBtn.Content = "Restart";

                    sentenceBtn.IsEnabled = false;
                    chapterBtn.IsEnabled = false;
                    saveBtb.IsEnabled = false;
                    menuPlay.IsEnabled = false;
                    playBtn.IsEnabled = false;
                    stopBtn.IsEnabled = false;

                    Task.Run(() => { ManualFileSaved(); });
                }
            }
        }

        /// <summary>
        /// Method fired after the manual file was saved.
        /// </summary>
        private async void ManualFileSaved()
        {
            await this.currentTab.ManualSave();

            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                playBtn.IsEnabled = true;
                stopBtn.IsEnabled = true;
                menuPlay.IsEnabled = true;
                sentenceBtn.IsEnabled = true;
                chapterBtn.IsEnabled = true;
            }));
        }

        /// <summary>
        /// Method fired after the user clicks on the Convert button.
        /// </summary>
        private void convert_Click(object sender, RoutedEventArgs e)
        {
            if (this.currentTab != null && this.currentTab.ManualState == State.Saved)
            {
                if (MessageBox.Show("This will convert the progress file to a timing file,\nthis can't be reversed. Are you sure you want to continue?", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    this.currentTab.ConvertProgressToTiming();
                    this.currentTab.Initialize();
                }
            }
        }

        #endregion

        #region Control Events

        /// <summary>
        /// Method fired when the user clicks on the play/pause button.
        /// </summary>
        private void Play_Clicked(object sender, RoutedEventArgs e)
        {
            if (this.audioPlayer.PlaybackState == PlaybackState.Playing)
            {
                this.audioPlayer.Pause();
                this.playBtn.IsPlaying = false;
                this.trackbarTimer.Stop();
            }
            else
            {
                this.audioPlayer.Play();
                this.playBtn.IsPlaying = true;
                this.trackbarTimer.Start();
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the stop button.
        /// </summary>
        private void Stop_Clicked(object sender, RoutedEventArgs e)
        {
            this.audioPlayer.Stop();
            this.playBtn.IsPlaying = false;
            this.trackbarTimer.Stop();
            this.timeElapsed.Text = "00:00:00";
            this.currentTab.CurrentAudioFile.CurrentTime = TimeSpan.Zero;
        }

        /// <summary>
        /// Method fired when the user clicks on the sync book button.
        /// </summary>
        private void SyncBook_Clicked(object sender, RoutedEventArgs e)
        {
            this.audioPlayer.Stop();
            this.trackbarTimer.Stop();
            this.playBtn.IsPlaying = false;

            TimeSpan currentTime = TimeSpan.Parse(this.timeElapsed.Text);
            string str = string.Empty;

            foreach (Chapter chapter in this.currentTab.Book.Chapters)
            {
                bool found = false;
                foreach (Sentence sentence in chapter.Sentences)
                {
                    if (sentence.FirstAudioPosition.Position <= currentTime && sentence.FirstAudioPosition.AudioFileIndex != -1)
                        str = sentence.OriginalText;
                    else
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    break;
            }

            if (!string.IsNullOrWhiteSpace(str))
            {
                DispHTMLBody body = ((DispHTMLBody)((DispHTMLDocument)this.currentTab.Browser.Document).body);
                IHTMLTxtRange range = body.createTextRange();

                if (range.findText(str, 0, 0))
                {
                    try
                    {
                        range.select();
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        System.Windows.MessageBox.Show(this, "Can't sync to book.");
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show(this, "There is no text for this part of the audio.");
                }
            }
            else
                MessageBox.Show(this, "There is no text for this part of the audio.");
        }

        /// <summary>
        /// Method fired when the user clicks on the previous button.
        /// </summary>
        private void Previous_Clicked(object sender, RoutedEventArgs e)
        {
            this.audioPlayer.Stop();

            TimeSpan totalTime = this.currentTab.CurrentAudioFile.CurrentTime;
            for (int i = 0; i < this.currentTab.CurrentAudioFileIndex; i++)
                totalTime += this.currentTab.Book.AudioFiles[i].TotalTime;

            int j = 0;
            for (j = 0; j < this.currentTab.Book.Chapters.Count; j++)
            {
                bool foundChapter = false;

                foreach (Sentence sentence in this.currentTab.Book.Chapters[j].Sentences)
                {
                    if (sentence.FirstAudioPosition.Position >= totalTime)
                    {
                        foundChapter = true;
                        break;
                    }
                }

                if (foundChapter)
                    break;
            }

            if (j > 0)
                j--;

            this.currentTab.SelectAudioFile(this.currentTab.Book.Chapters[j].Sentences[0].FirstAudioPosition.AudioFileIndex);

            TimeSpan timeToRemove = TimeSpan.Zero;
            for (int i = 0; i < this.currentTab.CurrentAudioFileIndex; i++)
                timeToRemove += this.currentTab.Book.AudioFiles[i].TotalTime;

            this.currentTab.CurrentAudioFile.CurrentTime = this.currentTab.Book.Chapters[j].Sentences[0].FirstAudioPosition.Position - timeToRemove;

            this.prevBtn.IsEnabled = (j > 0 && this.currentTab.Book.Chapters[j - 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j - 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
            this.menuPrev.IsEnabled = this.prevBtn.IsEnabled;

            this.nextBtn.IsEnabled = true;
            this.menuNext.IsEnabled = this.nextBtn.IsEnabled;

            this.currentFile.Text = Path.GetFileNameWithoutExtension(this.currentTab.CurrentAudioFile.FileName);

            this.audioPlayer = new WaveOut();
            this.audioPlayer.Init(this.currentTab.CurrentAudioFile);
            this.audioPlayer.Play();

            this.playBtn.IsPlaying = true;
            this.trackbarTimer.Start();
        }

        /// <summary>
        /// Method fired when the user clicks on the next button.
        /// </summary>
        private void Next_Clicked(object sender, RoutedEventArgs e)
        {
            this.audioPlayer.Stop();

            TimeSpan totalTime = this.currentTab.CurrentAudioFile.CurrentTime;
            for (int i = 0; i < this.currentTab.CurrentAudioFileIndex; i++)
                totalTime += this.currentTab.Book.AudioFiles[i].TotalTime;

            int j = 0;
            for (j = 0; j < this.currentTab.Book.Chapters.Count; j++)
            {
                bool foundChapter = false;

                foreach (Sentence sentence in this.currentTab.Book.Chapters[j].Sentences)
                {
                    if (sentence.FirstAudioPosition.Position >= totalTime)
                    {
                        foundChapter = true;
                        break;
                    }
                }

                if (foundChapter)
                    break;
            }

            if (j + 1 < this.currentTab.Book.Chapters.Count)
                j++;

            this.currentTab.SelectAudioFile(this.currentTab.Book.Chapters[j].Sentences[0].FirstAudioPosition.AudioFileIndex);
            TimeSpan timeToRemove = TimeSpan.Zero;
            for (int i = 0; i < this.currentTab.CurrentAudioFileIndex; i++)
                timeToRemove += this.currentTab.Book.AudioFiles[i].TotalTime;

            this.currentTab.CurrentAudioFile.CurrentTime = this.currentTab.Book.Chapters[j].Sentences[0].FirstAudioPosition.Position - timeToRemove;

            this.prevBtn.IsEnabled = true;
            this.menuPrev.IsEnabled = this.prevBtn.IsEnabled;

            this.nextBtn.IsEnabled = (j + 1 < this.currentTab.Book.Chapters.Count && this.currentTab.Book.Chapters[j + 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j + 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
            this.menuNext.IsEnabled = this.nextBtn.IsEnabled;

            this.currentFile.Text = Path.GetFileNameWithoutExtension(this.currentTab.CurrentAudioFile.FileName);

            this.audioPlayer = new WaveOut();
            this.audioPlayer.Init(this.currentTab.CurrentAudioFile);
            this.audioPlayer.Play();

            this.playBtn.IsPlaying = true;
            this.trackbarTimer.Start();
        }

        /// <summary>
        /// Method fired when the user change volume.
        /// </summary>
        private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue == 0)
                volImg.Image = new BitmapImage(new Uri("pack://application:,,,/Seshat;component/Assets/Normal/soundMute.png"));
            else if (e.NewValue > 0 && e.NewValue <= .25)
                volImg.Image = new BitmapImage(new Uri("pack://application:,,,/Seshat;component/Assets/Normal/sound1.png"));
            else if (e.NewValue > .25 && e.NewValue <= .50)
                volImg.Image = new BitmapImage(new Uri("pack://application:,,,/Seshat;component/Assets/Normal/sound2.png"));
            else if (e.NewValue > .50 && e.NewValue <= .75)
                volImg.Image = new BitmapImage(new Uri("pack://application:,,,/Seshat;component/Assets/Normal/sound3.png"));
            else if (e.NewValue > .75 && e.NewValue <= 1)
                volImg.Image = new BitmapImage(new Uri("pack://application:,,,/Seshat;component/Assets/Normal/sound4.png"));

            volImg.ImageHover = volImg.Image;

            if (this.currentTab != null && this.currentTab.CurrentAudioFile != null)
            {
                this.currentTab.Volume = (float)e.NewValue;
                this.audioPlayer.Volume = (float)e.NewValue;
            }
        }

        /// <summary>
        /// Method fired when the user change the timer.
        /// </summary>
        private void Time_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.currentTab != null && this.currentTab.CurrentAudioFile != null)
            {
                if (isMouseSlider)
                    this.trackbarTimer.Stop();

                TimeSpan targetTime = TimeSpan.FromSeconds(e.NewValue);
                TimeSpan totalTime = TimeSpan.Zero;

                int i = 0;
                for (i = 0; i < this.currentTab.Book.AudioFiles.Count; i++)
                {
                    if (totalTime + this.currentTab.Book.AudioFiles[i].TotalTime > targetTime)
                        break;

                    totalTime += this.currentTab.Book.AudioFiles[i].TotalTime;
                }

                if (isMouseSlider)
                {
                    if (this.currentTab.CurrentAudioFileIndex != i)
                    {
                        PlaybackState oldState = this.audioPlayer.PlaybackState;
                        this.audioPlayer.Stop();

                        this.currentTab.SelectAudioFile(i);

                        if (this.currentTab.CurrentAudioFile == null)
                        {
                            this.currentTab.SelectAudioFile(0);
                            this.currentTab.CurrentAudioFile.CurrentTime = TimeSpan.Zero;
                            this.audioPlayer = new WaveOut();
                            this.audioPlayer.Init(this.currentTab.CurrentAudioFile);
                            this.playBtn.IsPlaying = false;
                            this.isMouseSlider = false;
                        }
                        else
                        {
                            this.audioPlayer = new WaveOut();
                            this.audioPlayer.Init(this.currentTab.CurrentAudioFile);

                            this.currentFile.Text = Path.GetFileNameWithoutExtension(this.currentTab.CurrentAudioFile.FileName);

                            int j = 0;
                            for (j = 0; j < this.currentTab.Book.Chapters.Count; j++)
                            {
                                bool foundChapter = false;

                                foreach (Sentence sentence in this.currentTab.Book.Chapters[j].Sentences)
                                {
                                    if (sentence.FirstAudioPosition.Position >= totalTime)
                                    {
                                        foundChapter = true;
                                        break;
                                    }
                                }

                                if (foundChapter)
                                    break;
                            }

                            this.nextBtn.IsEnabled = (j + 1 < this.currentTab.Book.Chapters.Count && this.currentTab.Book.Chapters[j + 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j + 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
                            this.menuNext.IsEnabled = this.nextBtn.IsEnabled;

                            this.prevBtn.IsEnabled = (j > 0 && this.currentTab.Book.Chapters[j - 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j - 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
                            this.menuPrev.IsEnabled = this.prevBtn.IsEnabled;

                            if (oldState == PlaybackState.Playing)
                                this.audioPlayer.Play();
                        }
                    }

                    if (this.isMouseSlider)
                        this.currentTab.CurrentAudioFile.CurrentTime = targetTime - totalTime;
                }

                StringBuilder sb = new StringBuilder();
                sb.Append((targetTime.Days * 24 + targetTime.Hours).ToString("D2"));
                sb.Append(":");
                sb.Append(targetTime.Minutes.ToString("D2"));
                sb.Append(":");
                sb.Append(targetTime.Seconds.ToString("D2"));

                this.timeElapsed.Text = sb.ToString();
                                 
                string xmlFile = MainWindow.TimingsFolder + @"\" + this.currentTab.Book.Checksum + ".xml";
                lock (this.currentTab.xmlLock)
                {
                    if (File.Exists(xmlFile))
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(xmlFile);
                        ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("CurrentAudioFileIndex", Convert.ToString(this.currentTab.CurrentAudioFileIndex));
                        ((XmlElement)doc.GetElementsByTagName("Book").Item(0)).SetAttribute("CurrentAudioPosition", Convert.ToString(this.currentTab.CurrentAudioFile.CurrentTime));
                        doc.Save(xmlFile);
                    }
                }

                if (isMouseSlider)
                    this.trackbarTimer.Start();

                this.isMouseSlider = true;
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the sync audio button.
        /// </summary>
        private void SyncAudio_Clicked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.currentTab != null)
                {
                    this.trackbarTimer.Stop();

                    IHTMLDocument2 htmlDoc = this.currentTab.Browser.Document as IHTMLDocument2;

                    if (htmlDoc.selection.type == "Text")
                    {
                        IHTMLTxtRange range = (IHTMLTxtRange)htmlDoc.selection.createRange();

                        if (!string.IsNullOrWhiteSpace(range.text))
                        {
                            string content = Regex.Replace(range.text, @"\t|\n|\r", "");

                            string[] abbWithDots = new string[] { "Mr. ", "Mrs. ", "Ms. ", "Sir. ", "A.A ", "A.A.S ", "A.B.D ", "A.F.A ", "B.A. ", "B.F.A ", "B.S. ", "M.A. ", "M.Ed. ", "M.S. ", "Dr. ", "Esq. ", "Prof. ", "Ph.D. ", "M.D. ", "J.D. " };
                            string[] abbWithoutDots = new string[] { "Mr ", "Mrs ", "Ms ", "Sir ", "AA ", "AAS ", "ABD ", "AFA ", "BA ", "BFA ", "BS ", "MA ", "MEd ", "MS ", "Dr ", "Esq ", "Prof ", "PhD ", "MD ", "JD " };

                            for (int i = 0; i < abbWithDots.Length; i++)
                                content = content.Replace(abbWithDots[i], abbWithoutDots[i]);

                            string[] sentences = Regex.Split(content, @"(?<=[\.])");

                            for (int i = 0; i < sentences.Length; i++)
                            {
                                for (int j = 0; j < abbWithDots.Length; j++)
                                    sentences[i] = sentences[i].Replace(abbWithoutDots[j], abbWithDots[j]);
                            }

                            string firstSentence = sentences[0].Trim().RemoveChars('“', '’', '‘', '”');

                            List<Sentence> matches = new List<Sentence>();
                            foreach (Chapter chapter in this.currentTab.Book.Chapters)
                                foreach (Sentence sentence in chapter.Sentences)
                                    if (sentence.Text.Contains(firstSentence))
                                        matches.Add(sentence);

                            if (matches.Count == 1)
                            {
                                if (matches[0].FirstAudioPosition.AudioFileIndex == -1 || matches[0].FirstAudioPosition.AudioFileIndex >= this.currentTab.Book.AudioFiles.Count)
                                {
                                    MessageBox.Show(this, "Could not find any audio for this sentence.");
                                }
                                else
                                {
                                    if (this.audioPlayer.PlaybackState == PlaybackState.Playing)
                                        this.audioPlayer.Stop();

                                    this.currentTab.SelectAudioFile(matches[0].FirstAudioPosition.AudioFileIndex);
                                    this.currentFile.Text = System.IO.Path.GetFileNameWithoutExtension(this.currentTab.CurrentAudioFile.FileName);
                                    this.audioPlayer = new WaveOut();
                                    this.audioPlayer.Init(this.currentTab.CurrentAudioFile);
                                    this.playerSlider.Value = matches[0].FirstAudioPosition.Position.TotalSeconds;

                                    this.audioPlayer.Play();
                                    this.trackbarTimer.Start();
                                    this.playBtn.IsPlaying = true;
                                }
                            }
                            else
                                MessageBox.Show("Too many hits, try select another sentence.");
                        }
                    }
                    else
                        MessageBox.Show("No sentence is selected or something went wrong. Please select a (new) sentence nearby and try again.");
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Method fired when the user clicks on open book.
        /// </summary>
        private void OpenBook_Clicked(object sender, RoutedEventArgs e)
        {
            OpenFileDialog bookFile = new OpenFileDialog();
            bookFile.Title = "Choose an eBook";
            bookFile.Filter = "Electronic Publication|*.epub";

            Nullable<bool> result = bookFile.ShowDialog(this);

            if (result.Value && !string.IsNullOrWhiteSpace(bookFile.FileName) && Path.GetExtension(bookFile.FileName) == ".epub")
            {
                // Try load timings file, if it doesn't work prompt for audio files and ask if the user wants to create it now.
                BookTab tab = new BookTab(bookFile.FileName);
                tab.Closed += tabClosed;
                tab.LoadingCompleted += tabLoadingCompleted;
                tab.UpdateProgress += tabUpdateProgress;
                tab.Exception += tabException;

                // Open new tab.
                System.Threading.ThreadPool.QueueUserWorkItem((object state) =>
                {
                    TimingType initialized = tab.Initialize();

                    Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    {
                        tabControl.Items.Add(tab);

                        if (tabControl.Items.Count == 1)
                            tabControl.SelectedIndex = 0;
                    }));

                    if (initialized == TimingType.MISSING_AUDIO || initialized == TimingType.CORRUPT)
                    {
                        OpenFileDialog audioFiles = new OpenFileDialog();
                        audioFiles.Title = "Choose audio files";
                        audioFiles.Filter = "mp3|*.mp3";
                        audioFiles.Multiselect = true;
                        audioFiles.InitialDirectory = Path.GetDirectoryName(bookFile.FileName);

                        result = audioFiles.ShowDialog();

                        if (result.Value && audioFiles.FileNames.Length > 0)
                        {
                            bool allMp3 = true;
                            for (int i = 0; i < audioFiles.FileNames.Length; i++)
                            {
                                if (Path.GetExtension(audioFiles.FileNames[i]) != ".mp3")
                                {
                                    allMp3 = false;
                                    break;
                                }
                            }

                            if (allMp3)
                            {
                                tab.InitializeAudio(audioFiles.FileNames);
                            }
                            else
                            {
                                MessageBox.Show("You can only select .mp3 files.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                            MessageBox.Show("No audio files selected, you can import audio files later.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);                
                    }
                });
            }
        }

        /// <summary>
        /// Method fired when the user clicks on import audio.
        /// </summary>
        private void OpenAudio_Clicked(object sender, RoutedEventArgs e)
        {
            if (this.audioPlayer != null)
                this.audioPlayer.Stop();

            OpenFileDialog audioFiles = new OpenFileDialog();
            audioFiles.Title = "Choose audio files";
            audioFiles.Filter = "mp3|*.mp3";
            audioFiles.Multiselect = true;

            Nullable<bool> result = audioFiles.ShowDialog(this);

            if (result.Value && audioFiles.FileNames.Length > 0)
            {
                System.Threading.ThreadPool.QueueUserWorkItem((object state) =>
                {
                    this.currentTab.InitializeAudio(audioFiles.FileNames);
                });
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the exit menu.
        /// </summary>
        private void Exit_Clicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Method fired when the user clicks on the about menu.
        /// </summary>
        private void ShowAbout(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Adnan Dervisevic & Tobias Oskarsson\nDatateknisk Systemutveckling 2011\nUniversity West", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Method fired when the user clicks on the help menu.
        /// </summary>
        private void ShowHelp(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Welcome to Seshat! You are about to enter the wonderful world of eBook and Audiobook syncing.\n\nWhen it comes to using Seshat, it's quite simple. Load in an eBook in the ePub format, and audio files using either a chapter structure (e.g. one file per chapter) or one file for the whole book. Unfortunately we are not able to support any other formats right now, or alternate file structures for the audio.\n\nWhen you've loaded in the files you'll be able to create a timings file using speech recognition, estimation or manually. When a timings file has been created you'll see two buttons have become availible for you to use. One is the \"Sync Audio\" button and the other is \"Sync Book\". These both work similarly, but in very different ways.\n\nTo use \"Sync Audio\" simply select a sentence or passage in the text and click it to have the approximately appropriate location be synced up in the audio file. In essence, start listening from the spot you select to sync from.\n\nTo use \"Sync Book\", simply press it after you've listened to the file. It will then find the location in the text, based on the time of your player. Precision will again vary from book to book, but should in general be good.\n\nThank you for using Seshat!", "Help", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Method fired when the user clicks on the compare menu.
        /// </summary>
        private void CreateSpeechTiming_Clicked(object sender, RoutedEventArgs e)
        {
            if (this.currentTab != null)
            {
                if (!this.currentTab.IsLoaded)
                {
                    string xmlFile = MainWindow.TimingsFolder + @"\" + this.currentTab.Book.Checksum + ".progress";

                    if (File.Exists(xmlFile))
                    {
                        if (MessageBox.Show("A manual timing progress file already exists.\nDo you want to delete this file and start\ncreating a automatically timing file?", "Manually timing file exists", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            File.Delete(xmlFile);

                            this.currentTab.CreateSpeechTimingFile();
                            this.progressbarGrid.Visibility = Visibility.Visible;
                            this.preProgressTxt.Text = "Sync Progress: ";
                            this.afterProgressTxt.Text = "%";
                            this.menuCreateManualTiming.IsEnabled = false;
                            this.menuCreateSpeechTiming.IsEnabled = false;
                            this.menuCreateEstimatedTiming.IsEnabled = false;
                        }
                    }
                    else
                    {
                        this.currentTab.CreateSpeechTimingFile();
                        this.progressbarGrid.Visibility = Visibility.Visible;
                        this.preProgressTxt.Text = "Sync Progress: ";
                        this.afterProgressTxt.Text = "%";
                        this.menuCreateManualTiming.IsEnabled = false;
                        this.menuCreateSpeechTiming.IsEnabled = false;
                        this.menuCreateEstimatedTiming.IsEnabled = false;
                    }

                    if (this.currentTab.CreateManual)
                    {
                        Width -= 200;
                        tabGrid.ColumnDefinitions.RemoveAt(1);
                        manualGrid.Visibility = Visibility.Collapsed;
                        this.currentTab.CreateManual = false;

                        File.WriteAllText(this.currentTab.TabFolder + "/index.html", this.currentTab.Book.Content);
                        this.currentTab.Browser.Refresh(true);
                    }
                }
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the compare menu.
        /// </summary>
        private void CreateEstimatedTiming_Clicked(object sender, RoutedEventArgs e)
        {
            if (this.currentTab != null)
            {
                if (!this.currentTab.IsLoaded)
                {
                    string xmlFile = MainWindow.TimingsFolder + @"\" + this.currentTab.Book.Checksum + ".progress";

                    if (File.Exists(xmlFile))
                    {
                        if (MessageBox.Show("A manual timing progress file already exists.\nDo you want to delete this file and start\ncreating an estimated timing file?", "Manually timing file exists", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            File.Delete(xmlFile);

                            this.currentTab.CreateEstimateTimingFile();
                            this.progressbarGrid.Visibility = Visibility.Visible;
                            this.preProgressTxt.Text = "Sync Progress: ";
                            this.afterProgressTxt.Text = "%";
                            this.menuCreateManualTiming.IsEnabled = false;
                            this.menuCreateSpeechTiming.IsEnabled = false;
                            this.menuCreateEstimatedTiming.IsEnabled = false;
                        }
                    }
                    else
                    {
                        this.currentTab.CreateEstimateTimingFile();
                        this.progressbarGrid.Visibility = Visibility.Visible;
                        this.preProgressTxt.Text = "Sync Progress: ";
                        this.afterProgressTxt.Text = "%";
                        this.menuCreateManualTiming.IsEnabled = false;
                        this.menuCreateSpeechTiming.IsEnabled = false;
                        this.menuCreateEstimatedTiming.IsEnabled = false;
                    }

                    if (this.currentTab.CreateManual)
                    {
                        Width -= 200;
                        tabGrid.ColumnDefinitions.RemoveAt(1);
                        manualGrid.Visibility = Visibility.Collapsed;
                        this.currentTab.CreateManual = false;

                        File.WriteAllText(this.currentTab.TabFolder + "/index.html", this.currentTab.Book.Content);
                        this.currentTab.Browser.Refresh(true);
                    }
                }
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the compare menu.
        /// </summary>
        private void Compare_Clicked(object sender, RoutedEventArgs e)
        {
            CompareWindow tool = new CompareWindow();
            tool.Owner = this;
            tool.ShowDialog();
        }

        /// <summary>
        /// Method fired when the user clicks on the manual menu.
        /// </summary>
        private void Manual_Clicked(object sender, RoutedEventArgs e)
        {
            if (this.currentTab != null)
            {
                if (this.currentTab.CreateManual)
                {
                    Width -= 200;
                    tabGrid.ColumnDefinitions.RemoveAt(1);
                    manualGrid.Visibility = Visibility.Collapsed;
                    this.currentTab.CreateManual = false;

                    File.WriteAllText(this.currentTab.TabFolder + "/index.html", this.currentTab.Book.Content);
                    this.currentTab.Browser.Refresh(true);
                }
                else
                {
                    Width += 200;
                    tabGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(200) });
                    manualGrid.Visibility = Visibility.Visible;
                    this.currentTab.CreateManual = true;

                    string content = this.currentTab.Book.Content;
                    content = content.Replace(". ", "<font style=\"font-size: 15px; background-color: #000;\">.</font> ");
                    content = content.Replace(".</p>", "<font style=\"font-size: 15px; background-color: #000;\">.</font></p>");
                    content = content.Replace(".</span>", "<font style=\"font-size: 15px; background-color: #000;\">.</font></span>");
                    content = content.Replace(".<br", "<font style=\"font-size: 15px; background-color: #000;\">.</font><br");
                    content = content.Replace(".</h2>", "<font style=\"font-size: 15px; background-color: #000;\">.</font></h2>");
                    content = content.Replace(".’", "<font style=\"font-size: 15px; background-color: #000;\">.</font>’");
                    content = content.Replace(".”", "<font style=\"font-size: 15px; background-color: #000;\">.</font>”");
                    content = content.Replace(".,", "<font style=\"font-size: 15px; background-color: #000;\">.</font>,");
                    content = content.Replace(".)", "<font style=\"font-size: 15px; background-color: #000;\">.</font>)");
                    content = content.Replace(".?", "<font style=\"font-size: 15px; background-color: #000;\">.</font>?");

                    File.WriteAllText(this.currentTab.TabFolder + "/index.html", content);
                    this.currentTab.Browser.Refresh(true);
                }
            }
        }

        /// <summary>
        /// Method fired when the user clicks on the time elapsed box.
        /// </summary>
        private void specifyTime(object sender, MouseButtonEventArgs e)
        {
            if (this.currentTab != null && this.currentTab.CurrentAudioFile != null && this.currentTab.Book.AudioFiles != null)
            {
                TimeSpan totalTime = TimeSpan.Zero;

                for (int i = 0; i < this.currentTab.Book.AudioFiles.Count; i++)
                    totalTime += this.currentTab.Book.AudioFiles[i].TotalTime;

                if (totalTime == TimeSpan.Zero)
                    return;

                SpecifyTime specifyTime = new SpecifyTime(totalTime);
                specifyTime.Owner = this;

                if (specifyTime.ShowDialog().Value)
                {
                    Time_Changed(this, new RoutedPropertyChangedEventArgs<double>(0, specifyTime.Result));
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Saves the scroll value for the current tab.
        /// </summary>
        private void scrollTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (this.currentTab != null && this.currentTab.Browser.IsLoaded)
                    this.currentTab.SaveScrollValue();
            }));
        }

        /// <summary>
        /// Method fired when we should update the trackbar.
        /// </summary>
        private void trackbarTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (this.currentTab != null && this.currentTab.CurrentAudioFile != null)
                {
                    if (this.audioPlayer.PlaybackState == PlaybackState.Playing)
                    {
                        TimeSpan totalCurrent = this.currentTab.CurrentAudioFile.CurrentTime;

                        for (int i = 0; i < this.currentTab.CurrentAudioFileIndex; i++)
                            totalCurrent += this.currentTab.Book.AudioFiles[i].TotalTime;

                        StringBuilder sb = new StringBuilder();
                        sb.Append((totalCurrent.Days * 24 + totalCurrent.Hours).ToString("D2"));
                        sb.Append(":");
                        sb.Append(totalCurrent.Minutes.ToString("D2"));
                        sb.Append(":");
                        sb.Append(totalCurrent.Seconds.ToString("D2"));

                        this.timeElapsed.Text = sb.ToString();
                        this.isMouseSlider = false;
                        this.playerSlider.Value = totalCurrent.TotalSeconds;
                    }

                    if (this.currentTab.CurrentAudioFile.CurrentTime == this.currentTab.CurrentAudioFile.TotalTime)
                    {
                        this.audioPlayer.Stop();
                        if (this.currentTab.HasNextAudioFile())
                        {
                            this.currentTab.SelectAudioFile(this.currentTab.CurrentAudioFileIndex + 1);
                            this.currentFile.Text = Path.GetFileNameWithoutExtension(this.currentTab.CurrentAudioFile.FileName);

                            this.audioPlayer = new WaveOut();
                            this.audioPlayer.Init(this.currentTab.CurrentAudioFile);
                            this.audioPlayer.Play();
                        }
                    }
                }
            }));
        }

        /// <summary>
        /// Method fired when the system changes tab.
        /// </summary>
        private void TabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e == null || e.Source is System.Windows.Controls.TabControl)
            {
                if (audioPlayer != null && audioPlayer.PlaybackState != PlaybackState.Stopped)
                    audioPlayer.Stop();

                if (e != null)
                    if (e.AddedItems.Count > 0)
                        this.currentTab = (BookTab)e.AddedItems[0];

                if (tabControl.Items.Count == 0)
                    this.currentTab = null;

                if (this.currentTab != null && this.currentTab.CurrentAudioFile != null)
                {
                    this.currentFile.Text = Path.GetFileNameWithoutExtension(this.currentTab.CurrentAudioFile.FileName);
                    this.audioPlayer = new WaveOut();
                    this.audioPlayer.Init(this.currentTab.CurrentAudioFile);
                    this.menuPlay.IsEnabled = true;
                    this.playBtn.IsEnabled = true;
                    this.stopBtn.IsEnabled = true;

                    this.playerSlider.IsEnabled = true;

                    this.menuImportAudio.IsEnabled = false;
                    this.importAudioBtn.IsEnabled = false;

                    this.volImg.IsEnabled = true;
                    this.volumeSlider.IsEnabled = true;
                    this.volumeSlider.Value = this.currentTab.Volume;
                    this.audioPlayer.Volume = this.currentTab.Volume;

                    TimeSpan totalTime = TimeSpan.Zero;
                    TimeSpan currentTime = this.currentTab.CurrentAudioFile.CurrentTime;
                    for (int i = 0; i < this.currentTab.Book.AudioFiles.Count; i++)
                    {
                        totalTime += this.currentTab.Book.AudioFiles[i].TotalTime;
                        if (i < this.currentTab.CurrentAudioFileIndex)
                            currentTime += this.currentTab.Book.AudioFiles[i].TotalTime;
                    }

                    this.playerSlider.Value = currentTime.TotalSeconds;
                    this.playerSlider.Maximum = totalTime.TotalSeconds;
                    this.playerSlider.LargeChange = this.playerSlider.Maximum / 10;

                    StringBuilder sb = new StringBuilder();
                    sb.Append((totalTime.Days * 24 + totalTime.Hours).ToString("D2"));
                    sb.Append(":");
                    sb.Append(totalTime.Minutes.ToString("D2"));
                    sb.Append(":");
                    sb.Append(totalTime.Seconds.ToString("D2"));

                    this.timeTotal.Text = sb.ToString();
                    this.timeElapsed.Text = currentTime.ToString("hh\\:mm\\:ss");

                    this.listBox.Items.Clear();
                    int currentChapterPosition = 0;
                    foreach (KeyValuePair<int, List<AudioPosition>> audioPosition in this.currentTab.ManualTimings)
                    {
                        currentChapterPosition++;
                        listBox.Items.Add("Chapter " + currentChapterPosition);

                        for (int i = 0; i < audioPosition.Value.Count; i++)
                        {
                            listBox.Items.Add("Sentence " + (i + 1) + ": " + audioPosition.Value[i].Position);
                        }
                    }

                    int j = 0;
                    for (j = 0; j < this.currentTab.Book.Chapters.Count; j++)
                    {
                        bool foundChapter = false;

                        foreach (Sentence sentence in this.currentTab.Book.Chapters[j].Sentences)
                        {
                            if (sentence.FirstAudioPosition.Position >= currentTime)
                            {
                                foundChapter = true;
                                break;
                            }
                        }

                        if (foundChapter)
                            break;
                    }

                    this.nextBtn.IsEnabled = (j + 1 < this.currentTab.Book.Chapters.Count && this.currentTab.Book.Chapters[j + 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j + 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
                    this.menuNext.IsEnabled = this.nextBtn.IsEnabled;

                    this.prevBtn.IsEnabled = (j > 0 && this.currentTab.Book.Chapters[j - 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j - 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
                    this.menuPrev.IsEnabled = this.prevBtn.IsEnabled;

                    if (this.currentTab.IsLoaded)
                    {
                        this.menuSyncBook.IsEnabled = true;
                        this.menuSyncAudio.IsEnabled = true;
                        this.syncAudioBtn.IsEnabled = true;
                        this.syncAudioBtn.Visibility = Visibility.Visible;

                        this.syncBookBtn.IsEnabled = true;
                        this.menuCreateManualTiming.IsEnabled = false;
                        this.menuCreateSpeechTiming.IsEnabled = false;
                        this.menuCreateEstimatedTiming.IsEnabled = false;

                        this.progressbarGrid.Visibility = Visibility.Hidden;
                    }
                    else
                    {
                        this.menuSyncBook.IsEnabled = false;
                        this.menuSyncAudio.IsEnabled = false;
                        this.syncAudioBtn.IsEnabled = false;
                        this.syncAudioBtn.Visibility = Visibility.Hidden;
                        this.syncBookBtn.IsEnabled = false;

                        if (this.currentTab.IsAudioLoading)
                        {
                            if (!string.IsNullOrEmpty(this.currentTab.LoadingText))
                            {
                                System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                                {
                                    if (this.progressbarGrid.Visibility == Visibility.Hidden)
                                    {
                                        this.progressbarGrid.Visibility = Visibility.Visible;
                                        this.preProgressTxt.Text = string.Empty;
                                        this.progressTxt.Text = this.currentTab.LoadingText;
                                        this.afterProgressTxt.Text = string.Empty;
                                        this.progressBar.IsIndeterminate = true;
                                    }
                                    else
                                    {
                                        this.progressbarGrid.Visibility = Visibility.Hidden;
                                        this.preProgressTxt.Text = "Sync Progress: ";
                                        this.progressTxt.Text = "0";
                                        this.afterProgressTxt.Text = "%";
                                        this.progressBar.IsIndeterminate = false;
                                    }
                                }));
                            }
                            else
                            {
                                this.preProgressTxt.Text = "Sync Progress: ";
                                this.afterProgressTxt.Text = "%";
                                this.progressbarGrid.Visibility = Visibility.Visible;
                            }
                        }
                        else
                        {
                            this.menuCreateSpeechTiming.IsEnabled = true;
                            this.menuCreateManualTiming.IsEnabled = true;
                            this.menuCreateEstimatedTiming.IsEnabled = true;
                        }
                    }

                    if (this.currentTab.CreateManual)
                    {
                        Width += 200;
                        tabGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(200) });
                        manualGrid.Visibility = Visibility.Visible;
                        this.currentTab.CreateManual = true;

                        string content = this.currentTab.Book.Content;
                        content = content.Replace(".", "<font style=\"font-size: 15px; background-color: #000;\">.</font>");

                        File.WriteAllText(this.currentTab.TabFolder + "/index.html", content);
                        this.currentTab.Browser.Refresh(true);
                    }
                    else
                    {
                        if (tabGrid.ColumnDefinitions.Count > 1)
                        {
                            Width -= 200;
                            tabGrid.ColumnDefinitions.RemoveAt(1);
                            manualGrid.Visibility = Visibility.Collapsed;
                            this.currentTab.CreateManual = false;

                            File.WriteAllText(this.currentTab.TabFolder + "/index.html", this.currentTab.Book.Content);
                            this.currentTab.Browser.Refresh(true);
                        }
                    }
                    this.playBtn.IsPlaying = false;
                }
                else
                {
                    this.currentFile.Text = string.Empty;
                    this.menuPlay.IsEnabled = true;
                    this.playBtn.IsEnabled = false;
                    this.stopBtn.IsEnabled = false;

                    this.menuImportAudio.IsEnabled = true;
                    this.importAudioBtn.IsEnabled = true;

                    this.playerSlider.Value = 0;
                    this.playerSlider.Maximum = 10;
                    this.playerSlider.LargeChange = this.playerSlider.Maximum / 10;
                    this.playerSlider.IsEnabled = false;
                    this.timeTotal.Text = "00:00:00";
                    this.timeElapsed.Text = "00:00:00";

                    this.menuPrev.IsEnabled = false;
                    this.prevBtn.IsEnabled = false;

                    this.menuNext.IsEnabled = false;
                    this.nextBtn.IsEnabled = false;

                    this.volumeSlider.Value = 0;
                    this.volImg.IsEnabled = false;
                    this.volumeSlider.IsEnabled = false;

                    this.menuSyncBook.IsEnabled = false;
                    this.menuSyncAudio.IsEnabled = false;
                    this.syncAudioBtn.IsEnabled = false;
                    this.syncAudioBtn.Visibility = Visibility.Hidden;
                    this.syncBookBtn.IsEnabled = false;

                    this.menuCreateManualTiming.IsEnabled = false;
                    this.menuCreateSpeechTiming.IsEnabled = false;
                    this.menuCreateEstimatedTiming.IsEnabled = false;

                    this.progressbarGrid.Visibility = Visibility.Hidden;

                    if (tabGrid.ColumnDefinitions.Count > 1)
                    {
                        Width -= 200;
                        tabGrid.ColumnDefinitions.RemoveAt(1);
                        manualGrid.Visibility = Visibility.Collapsed;
                    }
                }

                if (this.currentTab == null)
                {
                    this.menuImportAudio.IsEnabled = false;
                    this.importAudioBtn.IsEnabled = false;
                }

                this.Title = (this.currentTab != null) ? this.currentTab.Book.Title : "Seshat";
            }
        }

        /// <summary>
        /// Method fired when the user closes the tab.
        /// </summary>
        private void tabClosed(object sender, FrameworkElement e)
        {
            FrameworkElement target = e;
            while (target is System.Windows.Controls.ContextMenu == false)
                target = target.Parent as FrameworkElement;

            var tabItem = (target as System.Windows.Controls.ContextMenu).PlacementTarget as TabItem;
            tabControl.Items.Remove(tabItem);
        }

        /// <summary>
        /// Method fired when a tab has completely loaded the timings file.
        /// </summary>
        private void tabLoadingCompleted(object sender, EventArgs e)
        {
            BookTab target = (BookTab)sender;
            if (currentTab == target)
            {
                if (!string.IsNullOrEmpty(this.currentTab.LoadingText))
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    {
                        if (this.progressbarGrid.Visibility == Visibility.Hidden)
                        {
                            this.progressbarGrid.Visibility = Visibility.Visible;
                            this.preProgressTxt.Text = string.Empty;
                            this.progressTxt.Text = this.currentTab.LoadingText;
                            this.afterProgressTxt.Text = string.Empty;
                            this.progressBar.IsIndeterminate = true;
                        }
                        else
                        {
                            this.progressbarGrid.Visibility = Visibility.Hidden;
                            this.preProgressTxt.Text = "Sync Progress: ";
                            this.progressTxt.Text = "0";
                            this.afterProgressTxt.Text = "%";
                            this.progressBar.IsIndeterminate = false;
                        }
                    }));
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    {
                        if (this.currentTab.CurrentAudioFile != null)
                        {
                            this.currentFile.Text = Path.GetFileNameWithoutExtension(this.currentTab.CurrentAudioFile.FileName);
                            this.audioPlayer = new WaveOut();
                            this.audioPlayer.Init(this.currentTab.CurrentAudioFile);
                            this.menuPlay.IsEnabled = true;
                            this.playBtn.IsEnabled = true;
                            this.stopBtn.IsEnabled = true;

                            this.playerSlider.IsEnabled = true;

                            this.menuImportAudio.IsEnabled = false;
                            this.importAudioBtn.IsEnabled = false;

                            this.volImg.IsEnabled = true;
                            this.volumeSlider.IsEnabled = true;
                            this.volumeSlider.Value = this.audioPlayer.Volume;

                            TimeSpan totalTime = TimeSpan.Zero;
                            TimeSpan currentTime = this.currentTab.CurrentAudioFile.CurrentTime;
                            for (int i = 0; i < this.currentTab.Book.AudioFiles.Count; i++)
                            {
                                totalTime += this.currentTab.Book.AudioFiles[i].TotalTime;
                                if (i < this.currentTab.CurrentAudioFileIndex)
                                    currentTime += this.currentTab.Book.AudioFiles[i].TotalTime;
                            }

                            this.playerSlider.Value = currentTime.TotalSeconds;
                            this.playerSlider.Maximum = totalTime.TotalSeconds;
                            this.playerSlider.LargeChange = this.playerSlider.Maximum / 10;

                            StringBuilder sb = new StringBuilder();
                            sb.Append((totalTime.Days * 24 + totalTime.Hours).ToString("D2"));
                            sb.Append(":");
                            sb.Append(totalTime.Minutes.ToString("D2"));
                            sb.Append(":");
                            sb.Append(totalTime.Seconds.ToString("D2"));

                            this.timeTotal.Text = sb.ToString();
                            this.timeElapsed.Text = currentTime.ToString("hh\\:mm\\:ss");

                            this.listBox.Items.Clear();
                            int currentChapterPosition = 0;
                            foreach (KeyValuePair<int, List<AudioPosition>> audioPosition in this.currentTab.ManualTimings)
                            {
                                currentChapterPosition++;
                                listBox.Items.Add("Chapter " + currentChapterPosition);

                                for (int i = 0; i < audioPosition.Value.Count; i++)
                                {
                                    listBox.Items.Add("Sentence " + (i + 1) + ": " + audioPosition.Value[i].Position);
                                }
                            }

                            int j = 0;
                            for (j = 0; j < this.currentTab.Book.Chapters.Count; j++)
                            {
                                bool foundChapter = false;

                                foreach (Sentence sentence in this.currentTab.Book.Chapters[j].Sentences)
                                {
                                    if (sentence.FirstAudioPosition.Position >= currentTime)
                                    {
                                        foundChapter = true;
                                        break;
                                    }
                                }

                                if (foundChapter)
                                    break;
                            }

                            this.nextBtn.IsEnabled = (j + 1 < this.currentTab.Book.Chapters.Count && this.currentTab.Book.Chapters[j + 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j + 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
                            this.menuNext.IsEnabled = this.nextBtn.IsEnabled;

                            this.prevBtn.IsEnabled = (j > 0 && this.currentTab.Book.Chapters[j - 1].Sentences.Count > 0 && this.currentTab.Book.Chapters[j - 1].Sentences[0].FirstAudioPosition.AudioFileIndex != -1);
                            this.menuPrev.IsEnabled = this.prevBtn.IsEnabled;

                            if (this.currentTab.IsLoaded)
                            {
                                this.menuSyncBook.IsEnabled = true;
                                this.menuSyncAudio.IsEnabled = true;
                                this.syncAudioBtn.IsEnabled = true;
                                this.syncAudioBtn.Visibility = Visibility.Visible;
                                this.syncBookBtn.IsEnabled = true;
                                this.menuCreateManualTiming.IsEnabled = false;
                                this.menuCreateSpeechTiming.IsEnabled = false;
                                this.menuCreateEstimatedTiming.IsEnabled = false;

                                this.progressbarGrid.Visibility = Visibility.Hidden;
                            }
                            else
                            {
                                this.menuSyncBook.IsEnabled = false;
                                this.menuSyncAudio.IsEnabled = false;
                                this.syncAudioBtn.IsEnabled = false;
                                this.syncAudioBtn.Visibility = Visibility.Hidden;
                                this.syncBookBtn.IsEnabled = false;

                                if (this.currentTab.IsAudioLoading)
                                {
                                    this.preProgressTxt.Text = "Sync Progress: ";
                                    this.afterProgressTxt.Text = "%";
                                    this.progressbarGrid.Visibility = Visibility.Visible;
                                }
                                else
                                {
                                    this.menuCreateSpeechTiming.IsEnabled = true;
                                    this.menuCreateManualTiming.IsEnabled = true;
                                    this.menuCreateEstimatedTiming.IsEnabled = true;
                                }
                            }

                            this.playBtn.IsPlaying = false;
                        }
                    }));
                }
            }
        }

        /// <summary>
        /// Method fired every time tab is doing any progress.
        /// </summary>
        private void tabUpdateProgress(object sender, double e)
        {
            BookTab target = (BookTab)sender;
            if (target == this.currentTab)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    this.progressbarGrid.Visibility = Visibility.Visible;
                    this.progressTxt.Text = Convert.ToString(Math.Round(e, 2));
                    this.progressBar.Value = Math.Round(e, 2);
                }));
            }
        }

        /// <summary>
        /// Method fired when a tab throws an exception.
        /// </summary>
        private void tabException(object sender, string e)
        {
            BookTab target = (BookTab)sender;
            if (currentTab == target)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    this.progressbarGrid.Visibility = Visibility.Hidden;

                    this.menuSyncAudio.IsEnabled = false;
                    this.menuSyncBook.IsEnabled = false;
                    this.syncAudioBtn.IsEnabled = false;
                    this.syncBookBtn.IsEnabled = false;
                }));
            }

            if (!string.IsNullOrWhiteSpace(e))
                MessageBox.Show(e, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Method fired when the user closes the program.
        /// </summary>
        private void Window_Closed(object sender, EventArgs e)
        {
            this.scrollTimer.Stop();
            this.trackbarTimer.Stop();

            DirectoryInfo info = new DirectoryInfo(Directory.GetCurrentDirectory());
            Regex regex = new Regex(@"^(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}$");
            foreach (DirectoryInfo dir in info.GetDirectories())
            {
                if (regex.IsMatch(dir.Name))
                    dir.Delete(true);
            }

            this.Dispose();
        }

        #endregion
    }
}