using System.ComponentModel.DataAnnotations;

namespace RedisChat.Models
{
    public class UserGroupChat
    {
        [Required(ErrorMessage = "Vui lòng nhập tên người dùng")]
        [RegularExpression("^[a-zA-Z\\s]*$", ErrorMessage = "Tên người dùng chỉ được chứa chữ cái và khoảng trắng")]
        public string UserName { get; set; }
    }
}
