using System;

namespace RedisChat.Models
{
    public class MessageChatRequest
    {
        public long IdUser { get; set; }
        public string RoomCode { get; set; }
        public string MessageContent { get; set; }
        public DateTime SendDate { get; set; } = DateTime.UtcNow;
    }
}
