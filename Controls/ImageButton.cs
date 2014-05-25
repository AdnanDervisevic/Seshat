#region File Description
/*
 * ImageButton
 * 
 * Copyright (C) Untitled. All Rights Reserved.
 */
#endregion

#region Using Statements
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
#endregion

namespace Seshat.Controls
{
    /// <summary>
    /// Class representing a image button with different 
    /// images for enabled, disabled and hover.
    /// </summary>
    public class ImageButton : Button
    {
        #region Fields

        /// <summary>
        /// The Image property.
        /// </summary>
        public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register("Image", typeof(ImageSource),
                typeof(ImageButton), new PropertyMetadata(default(ImageSource)));

        /// <summary>
        /// The Image disabled property.
        /// </summary>
        public static readonly DependencyProperty ImageDisabledProperty =
            DependencyProperty.Register("ImageDisabled", typeof(ImageSource),
                typeof(ImageButton), new PropertyMetadata(default(ImageSource)));

        /// <summary>
        /// The Image hover property.
        /// </summary>
        public static readonly DependencyProperty ImageHoverProperty =
            DependencyProperty.Register("ImageHover", typeof(ImageSource),
                typeof(ImageButton), new PropertyMetadata(default(ImageSource)));

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the image.
        /// </summary>
        public ImageSource Image
        {
            get { return (ImageSource)GetValue(ImageProperty); }
            set { SetValue(ImageProperty, value); }
        }

        /// <summary>
        /// Gets or sets the image that should be shown when the button is disabled.
        /// </summary>
        public ImageSource ImageDisabled
        {
            get { return (ImageSource)GetValue(ImageDisabledProperty); }
            set { SetValue(ImageDisabledProperty, value); }
        }

        /// <summary>
        /// Gets or sets the image that should be shown when hovering the button.
        /// </summary>
        public ImageSource ImageHover
        {
            get { return (ImageSource)GetValue(ImageHoverProperty); }
            set { SetValue(ImageHoverProperty, value); }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Static constructor.
        /// </summary>
        static ImageButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ImageButton),
                new FrameworkPropertyMetadata(typeof(ImageButton)));
        }

        #endregion
    }
}