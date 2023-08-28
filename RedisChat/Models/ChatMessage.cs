using System;

namespace RedisChat.Models
{
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string UserName { get; set; } = null;
        public string GroupName { get; set; } = null;
        public string MessageContent { get; set; } = null;
        //public string ImageContent { get; set; } = null;
        //public string AudioContent { get; set; } = null;
        //public string FileContent { get; set; } = null;
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
    }

}
