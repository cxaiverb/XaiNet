using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Diagnostics;

namespace XaiNet2.Helpers
{
    public static class NotificationHelper
    {
        public static void ShowToast(string title, string message, ToastScenario scenario = ToastScenario.Default)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .SetToastScenario(scenario)
                    .Show(); // Display the toast
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Notification error: {ex.Message}");
            }
        }
    }
}
