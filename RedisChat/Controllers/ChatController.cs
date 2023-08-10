using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using RedisChat.Models;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

public class ChatController : Controller
{
    private readonly IConnectionMultiplexer _redisConnection;
    private readonly ChatHub _hubContext;
    public ChatController(IConnectionMultiplexer redisConnection, ChatHub hubContext)
    {
        _redisConnection = redisConnection;
        _hubContext = hubContext;
    }
    #region Form hiển thị tin nhắn
    public async Task<IActionResult> Index(string groupName)
    {
        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.GroupName = groupName;
        var db = _redisConnection.GetDatabase();
        var chatMessages = db.ListRange($"{groupName}");

        List<ChatMessage> chatMessageList = new List<ChatMessage>();

        foreach (var redisMessage in chatMessages)
        {
            var messageString = (string)redisMessage;
            var chatMessage = JsonConvert.DeserializeObject<ChatMessage>(messageString);
            chatMessageList.Add(chatMessage);
        }
        return View(chatMessageList);
    }
    #endregion

    #region Form Nhập Tên Người Tham Gia
    public IActionResult JoinOrCreateGroupChat()
    {
        var model = new UserGroupChat();
        return View(model);
    }
    [HttpPost]
    public IActionResult JoinOrCreateGroupChat(UserGroupChat model)
    {
        if (ModelState.IsValid)
        {
            HttpContext.Session.SetString("UserName", model.UserName);
            return RedirectToAction("ChooseAction");
        }
        return View(model);
    }
    #endregion

    #region Form Join Hoặc Create Group Chat
    public IActionResult ChooseAction()
    {
        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        return View();
    }
    [HttpPost]
    public IActionResult ChooseAction(string createGroupChat)
    {
        if (createGroupChat == "true")
        {
            return RedirectToAction("GroupChats");
        }
        else
        {
            return RedirectToAction("JoinGroupChat");
        }
    }
    #endregion

    #region Form Create Group Chat
    public IActionResult GroupChats()
    {
        var userName = HttpContext.Session.GetString("UserName");
        var groupChats = GetGroupChatsFromRedis();

        if (!string.IsNullOrEmpty(userName))
        {
            groupChats = groupChats.Where(group => group.CreateBy == userName).ToList();
        }

        return View(groupChats);
    }
    [HttpPost]
    public IActionResult CreateGroupChats(GroupChat model)
    {
        if (ModelState.IsValid)
        {
            var groupChats = GetGroupChatsFromRedis();
            model.CreateBy = HttpContext.Session.GetString("UserName");

            var serializedGroupChats = JsonConvert.SerializeObject(model);
            var db = _redisConnection.GetDatabase();

            var keyGroup = "GroupChats";
            if (!string.IsNullOrEmpty(serializedGroupChats) && !groupChats.Any(gc => gc.GroupName == model.GroupName))
            {
                db.ListRightPush(keyGroup, serializedGroupChats);
                if (db.ListLength(keyGroup) > 5)
                {
                    db.ListLeftPop(keyGroup);
                }
            }


            return RedirectToAction("GroupChats");
        }
        var existingGroupChats = GetGroupChatsFromRedis();
        if (!string.IsNullOrEmpty(model.CreateBy))
        {
            existingGroupChats = existingGroupChats.Where(group => group.CreateBy == model.CreateBy).ToList();
        }
        return View(existingGroupChats);
    }

    private List<GroupChat> GetGroupChatsFromRedis()
    {
        var db = _redisConnection.GetDatabase();
        var keyGroup = "GroupChats";

        var existingGroupChats = db.ListRange(keyGroup, 0, -1);

        if (existingGroupChats.Length > 0)
        {
            var groupChats = new List<GroupChat>();
            foreach (var chatBytes in existingGroupChats)
            {
                var chat = Encoding.UTF8.GetString(chatBytes);
                // Bỏ dấu ngoặc vuông [ ] bên ngoài
                if (chat.StartsWith("[") && chat.EndsWith("]"))
                {
                    chat = chat.Substring(1, chat.Length - 2);
                }
                groupChats.Add(JsonConvert.DeserializeObject<GroupChat>(chat));
            }
            var result = groupChats.ToList();
            return result;
        }

        return new List<GroupChat>();
    }
    #endregion

    #region Form Join Group Chat
    public async Task<IActionResult> JoinGroupChat()
    {
        var userName = HttpContext.Session.GetString("UserName");
        ViewBag.UserName = userName;
        return View();
    }
    // POST: Xử lý việc nhập mã phòng và mật khẩu khi tham gia nhóm chat
    [HttpPost]
    public async Task<IActionResult> JoinGroupChat(GroupChat model)
    {
        var db = _redisConnection.GetDatabase();
        var listGroup = db.ListRange("GroupChats");
        List<GroupChat> groupList = new List<GroupChat>();
        foreach (var redisGroupChat in listGroup)
        {
            var groupString = (string)redisGroupChat; // Chuyển RedisValue thành string
            var groupChat = JsonConvert.DeserializeObject<GroupChat>(groupString);
            groupList.Add(groupChat);
        }

        if (groupList.Any(c => c.GroupName == model.GroupName && c.Password == model.Password))
        {
            return RedirectToAction("Index", new
            {
                groupName = model.GroupName
            });
        }
        return View(model);
    }
    #endregion

    #region Form Gửi Tin Nhắn
    public IActionResult SendMessage(string userName, string groupName)
    {
        ViewBag.UserName = userName;
        ViewBag.GroupChatCode = groupName;
        return View();
    }

    [HttpPost]
    public IActionResult SendMessage(ChatMessage message)
    {
        // Thêm tin nhắn mới vào Redis
        var db = _redisConnection.GetDatabase();
        var serializeMessage = JsonConvert.SerializeObject(message);
        db.ListRightPush($"{message.GroupName}", serializeMessage);
        // Xóa tin nhắn đầu tiên nếu có nhiều hơn 30 tin nhắn
        if (db.ListLength($"{message.GroupName}") > 30)
        {
            db.ListLeftPop($"{message.GroupName}");
        }

        db.Publish(message.GroupName, serializeMessage);

        return RedirectToAction("Index", new { groupName = message.GroupName });
    }
    #endregion

    #region Form Create Group Chat
    public IActionResult AllGroupChat()
    {
        var userName = HttpContext.Session.GetString("UserName");
        ViewBag.UserName = userName;
        var groupChats = GetGroupChatsFromRedis();
        if (groupChats.Count() > 0)
        {
            groupChats = groupChats.Where(group => group.Public == "true").ToList();
        }
        return View(groupChats);
    }
    #endregion

    #region Xoá Dữ Liệu Key Redis
    public IActionResult ClearChat()
    {
        var db = _redisConnection.GetDatabase();
        db.KeyDelete("Hoang");
        return RedirectToAction("JoinOrCreateGroupChat");
    }
    #endregion

    #region Xoá Toàn Bộ Key Redis
    public IActionResult DeleteKeys()
    {
        // Lấy danh sách các keys
        var keys = _redisConnection.GetServer("redis-16227.c278.us-east-1-4.ec2.cloud.redislabs.com:16227").Keys(database: 0, pattern: "*");
        // Xóa các keys
        foreach (var key in keys)
        {
            _redisConnection.GetDatabase().KeyDelete(key);
        }
        return RedirectToAction("JoinOrCreateGroupChat");
    }
    #endregion
}


