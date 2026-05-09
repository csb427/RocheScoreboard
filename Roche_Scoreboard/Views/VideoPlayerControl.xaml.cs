using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class VideoPlayerControl : UserControl
    {
        private Uri? _source;
        private bool _isDisposed;

        public VideoPlayerControl()
        {
            InitializeComponent();
            VideoPlayer.MediaEnded += OnMediaEnded;
            VideoPlayer.MediaFailed += OnMediaFailed;
            Unloaded += (_, _) => Cleanup();
        }

        private void Cleanup()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            VideoPlayer.MediaEnded -= OnMediaEnded;
            VideoPlayer.MediaFailed -= OnMediaFailed;
            Stop();
        }

        /// <summary>Sets the video source without starting playback.</summary>
        public void SetVideoSource(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            try
            {
                _source = new Uri(filePath, UriKind.Absolute);
            }
            catch (UriFormatException)
            {
                _source = null;
            }
        }

        /// <summary>Starts (or restarts) playback of the configured source.</summary>
        public void Play()
        {
            if (_source == null)
                return;

            try
            {
                VideoPlayer.Source = _source;
                VideoPlayer.Position = TimeSpan.Zero;
                VideoPlayer.Play();
            }
            catch
            {
                VideoPlayer.Source = null;
            }
        }

        /// <summary>Stops playback and clears the display.</summary>
        public void Stop()
        {
            try
            {
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
            }
            catch
            {
                // Ignore — control may already be unloaded
            }
        }

        private void OnMediaEnded(object? sender, RoutedEventArgs e)
        {
            // Loop the video
            try
            {
                VideoPlayer.Position = TimeSpan.Zero;
                VideoPlayer.Play();
            }
            catch
            {
                // Source may have been cleared mid-event
            }
        }

        private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            // Video codec unsupported or file corrupted — clear source
            try
            {
                VideoPlayer.Source = null;
            }
            catch
            {
                // Ignore
            }
        }
    }
}
