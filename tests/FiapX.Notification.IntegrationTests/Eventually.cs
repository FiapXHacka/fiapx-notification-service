namespace FiapX.Notification.IntegrationTests;

internal static class Eventually
{
    /// <summary>Polls until the condition holds; asynchronous consumers need a moment to catch up.</summary>
    public static async Task TrueAsync(Func<Task<bool>> condition, string because, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Condition not met within {timeoutSeconds}s: {because}");
    }
}
