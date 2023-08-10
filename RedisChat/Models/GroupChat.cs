using System.ComponentModel.DataAnnotations;

namespace RedisChat.Models
{
    public class GroupChat
    {
        [Required(ErrorMessage = "Vui lòng tạo GroupName")]
        public string GroupName { get; set; }
        public string Password { get; set; }
        public string CreateBy { get; set; }
        [Required(ErrorMessage = "Vui lòng để trạng thái")]
        public string Public { get; set; }
    }
}
