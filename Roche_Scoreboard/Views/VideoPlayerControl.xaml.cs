using System;
using System.Windows;
using System.Windows.Controls;

namespace Roche_Scoreboard.Views
{
    public partial class VideoPlayerControl : System.Windows.Controls.UserControl
    {
        private Uri? _source;

        public VideoPlayerControl()
        {
            InitializeComponent();
            VideoPlayer.MediaEnded += OnMediaEnded;
            VideoPlayer.MediaFailed += OnMediaFailed;
        }

        /// <summary>Sets the video source without starting playback.</summary>
        public void SetVideoSource(string filePath)
        {
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
            if (_source == null) return;
            try
            {
                VideoPlayer.Source = _source;
                VideoPlayer.Position = TimeSpan.Zero;
                VideoPlayer.Play();
            }
            catch
            {
                // Silently fail — video format may be unsupported
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
            // Video codec unsupported or file corrupted — clear source to avoid repeated failures
            try { VideoPlayer.Source = null; }
            catch { /* ignore */ }
        }
    }
}
