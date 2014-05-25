#region File Description
/*
 * PlayButton
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System.Windows;
using System.Windows.Media;
#endregion

namespace Seshat.Controls
{
    /// <summary>
    /// Class representing a play button with two different modes.
    /// Playing / Not Playing with different images.
    /// </summary>
    public class PlayButton : ImageButton
    {
        #region Fields

        /// <summary>
        /// The Image property.
        /// </summary>
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register("IsPlaying", typeof(bool),
                typeof(PlayButton), new PropertyMetadata(default(bool)));

        /// <summary>
        /// The Image pause property
        /// </summary>
        public static readonly DependencyProperty ImagePauseProperty =
            DependencyProperty.Register("ImagePause", typeof(ImageSource),
                typeof(PlayButton), new PropertyMetadata(default(ImageSource)));

        /// <summary>
        /// The Image pause property
        /// </summary>
        public static readonly DependencyProperty ImageHoverPauseProperty =
            DependencyProperty.Register("ImageHoverPause", typeof(ImageSource),
                typeof(PlayButton), new PropertyMetadata(default(ImageSource)));

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets whether we're playing or not.
        /// </summary>
        public bool IsPlaying
        {
            get { return (bool)GetValue(IsPlayingProperty); }
            set { SetValue(IsPlayingProperty, value); }
        }

        /// <summary>
        /// Gets or sets the image that should be shown when we're in paused state.
        /// </summary>
        public ImageSource PausedImage
        {
            get { return (ImageSource)GetValue(ImagePauseProperty); }
            set { SetValue(ImagePauseProperty, value); }
        }

        /// <summary>
        /// Gets or sets the image that should be shown when we're in paused state.
        /// </summary>
        public ImageSource PausedHoverImage
        {
            get { return (ImageSource)GetValue(ImageHoverPauseProperty); }
            set { SetValue(ImageHoverPauseProperty, value); }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Static constructor.
        /// </summary>
        static PlayButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PlayButton),
                new FrameworkPropertyMetadata(typeof(PlayButton)));
        }

        #endregion
    }
}