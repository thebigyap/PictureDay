using System;
using System.Threading;
using PictureDay.Utils;

namespace PictureDay.Services
{
    public class ActivityMonitor
    {
        private const int ActiveThresholdMinutes = 5;
        private readonly object _lockObject = new object();

        public bool IsUserActive()
        {
            lock (_lockObject)
            {
                try
                {
                    uint lastInputTime = WindowHelper.GetLastInputTime();
                    uint currentTime = (uint)Environment.TickCount;

                    uint idleTime;
                    if (currentTime >= lastInputTime)
                    {
                        idleTime = currentTime - lastInputTime;
                    }
                    else
                    {
                        idleTime = (uint.MaxValue - lastInputTime) + currentTime;
                    }

                    double idleMinutes = idleTime / 60000.0;

                    return idleMinutes <= ActiveThresholdMinutes;
                }
                catch
                {
                    return true;
                }
            }
        }

        public TimeSpan GetIdleTime()
        {
            lock (_lockObject)
            {
                try
                {
                    uint lastInputTime = WindowHelper.GetLastInputTime();
                    uint currentTime = (uint)Environment.TickCount;

                    uint idleTime;
                    if (currentTime >= lastInputTime)
                    {
                        idleTime = currentTime - lastInputTime;
                    }
                    else
                    {
                        idleTime = (uint.MaxValue - lastInputTime) + currentTime;
                    }

                    return TimeSpan.FromMilliseconds(idleTime);
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }
        }
    }
}