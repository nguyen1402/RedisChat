using System;

namespace RedisChat.Models
{
    public class ChatMessage
    {
        public string UserName { get; set; }
        public string GroupName { get; set; }
        public string MessageContent { get; set; }
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
    }

}
