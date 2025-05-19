namespace WinNotificationsFromMqtt.App
{
    public class AppConfiguration
    {
        public string Url { get; set; }
        public int Port { get; set; } = 1883;
        public string UserName { get; set; }
        public string Password { get; set; }
        public string ClientId { get; set; }
    }
}
