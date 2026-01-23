using System;
using System.Windows.Forms;

namespace PictureDay.Services
{
	public class NotificationService
	{
		private readonly NotifyIcon _notifyIcon;
		private DateTime _lastScheduledTimeUpdateUtc = DateTime.MinValue;

		public NotificationService(NotifyIcon notifyIcon)
		{
			_notifyIcon = notifyIcon ?? throw new ArgumentNullException(nameof(notifyIcon));
		}

		public void ShowScheduledMainPhotoTime(TimeSpan scheduledTime)
		{
			string greeting = GetGreeting(DateTime.Now);
			DateTime when = DateTime.Today.Add(scheduledTime);
			_notifyIcon.ShowBalloonTip(
				3000,
				"PictureDay",
				$"{greeting}! Today's photo is scheduled for {when:t}.",
				ToolTipIcon.Info);
		}

		private static string GetGreeting(DateTime now)
		{
			int hour = now.Hour;

			if (hour >= 5 && hour < 12)
			{
				return "Good morning";
			}

			if (hour >= 12 && hour < 17)
			{
				return "Good afternoon";
			}

			if (hour >= 17 && hour < 22)
			{
				return "Good evening";
			}

			return "Good night";
		}

		public void ShowScheduledMainPhotoTimeUpdated(TimeSpan scheduledTime)
		{
			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc - _lastScheduledTimeUpdateUtc < TimeSpan.FromMinutes(1))
			{
				return;
			}

			_lastScheduledTimeUpdateUtc = nowUtc;

			DateTime when = DateTime.Today.Add(scheduledTime);
			_notifyIcon.ShowBalloonTip(
				3000,
				"PictureDay",
				$"Update: Today's photo is now scheduled for {when:t}.",
				ToolTipIcon.Info);
		}

		public void ShowMainPhotoTaken()
		{
			_notifyIcon.ShowBalloonTip(
				3000,
				"PictureDay",
				"Photo captured.",
				ToolTipIcon.Info);
		}

		public void ShowMainPhotoAlreadyTaken()
		{
			_notifyIcon.ShowBalloonTip(
				3000,
				"PictureDay",
				"Today's main photo is already taken.",
				ToolTipIcon.Info);
		}
	}
}

