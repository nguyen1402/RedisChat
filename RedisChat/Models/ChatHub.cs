using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Timers;
using RedisChat.Models;
using System.Linq;
using StackExchange.Redis;
using System.Text.Json;
using System.Reflection;

public class ChatHub : Hub
{
    private readonly IConnectionMultiplexer _redisConnection;
    private ChatMessage _chatMessage;
    //Sử dụng để cài thời gian ping lại phía client
    private Timer _pingTimer;

    //Sử dụng để cài check hoạt động của người dùng
    private Timer _activityCheckTimer;

    //Sử dụng để thực hiện đếm số lượng người trong phòng 
    private readonly Dictionary<string, int> _roomOccupancy = new Dictionary<string, int>();

    //Sử dụng để lưu lại thời gian hoạt  động gần nhất của người dùng
    private Dictionary<string, DateTime> _userLastActivity = new Dictionary<string, DateTime>();


    //Sử dụng để lưu lại thông tin người dùng tham gia vào phòng chat nào
    private readonly Dictionary<string, ConnectionInfo> _connectionInfoMap = new Dictionary<string, ConnectionInfo>();

    public ChatHub(IConnectionMultiplexer redisConnection)
    {
        _redisConnection = redisConnection;
    }

    #region Cứ 10s thực hiện lại ping đến client 1 lần đê duy trì kết nối
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.GetHttpContext().Request.Query["tenantId"];
        var workgroupId = Context.GetHttpContext().Request.Query["workgroupId"];

        _pingTimer = new Timer(10000); // 10s
        _pingTimer.Elapsed += async (sender, e) => await PingClient();
        _pingTimer.Start();


        _activityCheckTimer = new Timer(600000); // 10p
        _activityCheckTimer.Elapsed += async (sender, e) => await CheckConnectionActivity();
        _activityCheckTimer.Start();

        await base.OnConnectedAsync();
    }

    private async Task PingClient()
    {
        await Clients.Client(Context.ConnectionId).SendAsync("Ping");
    }
    #endregion

    #region Kiểm tra connnection có hoạt động
    public async Task Pong()
    {
    }
    #endregion

    #region Kiểm tra dọn dẹp khi người dùng không làm gì trong 10 phút
    private async Task CheckConnectionActivity()
    {
        var now = DateTime.UtcNow;

        foreach (var connectionId in _userLastActivity.Keys.ToList())
        {
            if (_userLastActivity.TryGetValue(connectionId, out var lastActivity) && now - lastActivity > TimeSpan.FromMinutes(10))
            {
                if (_connectionInfoMap.TryGetValue(connectionId, out var connectionInfo))
                {
                    if (connectionInfo.ConnectionId == connectionId)
                    {
                        await Clients.Clients(connectionId).SendAsync("ReceiveMessage", new ChatMessage
                        {
                            UserName = "Hệ thống",
                            MessageContent = "Bạn đã bị đóng kết nối do treo quá lâu !"
                        });
                        await Groups.RemoveFromGroupAsync(connectionId, connectionInfo.RoomCode);
                        _connectionInfoMap.Remove(connectionId);
                        _userLastActivity.Remove(connectionId);
                    }
                }
            }
        }
    }
    #endregion

    #region Thực hiện thêm Connext của người dùng vào phòng chat
    public async Task JoinRoomAsync(string roomCode, string userId)
    {
        if (_roomOccupancy.TryGetValue(roomCode, out int currentOccupancy))
        {
            if (currentOccupancy >= 999)
            {
                _chatMessage = new ChatMessage
                {
                    UserName = "Hệ thống",
                    GroupName = roomCode,
                    MessageContent = "Phòng chat đã đầy!",
                };
                await Clients.Client(Context.ConnectionId).SendAsync("ReceiveMessage", _chatMessage);
                return;
            }
            _roomOccupancy[roomCode]++;
        }
        else
        {
            _roomOccupancy.Add(roomCode, 1);
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var connectionInfo = new ConnectionInfo
        {
            ConnectionId = Context.ConnectionId,
            UserId = userId,
            RoomCode = roomCode
        };
        _connectionInfoMap[Context.ConnectionId] = connectionInfo;

        _userLastActivity[Context.ConnectionId] = DateTime.UtcNow;
        _chatMessage = new ChatMessage
        {
            UserName = userId,
            GroupName = roomCode,
            MessageContent = "Đã tham gia phòng chat!",
        };
        await Clients.Group(roomCode).SendAsync("ReceiveMessage", _chatMessage);
    }
    #endregion

    #region Kick người dùng
    public async Task KickUserAsync(string userId, string roomCode)
    {
        var connectionIdsToKick = _connectionInfoMap.Values.Where(info => info.UserId == userId && info.RoomCode == roomCode)
            .Select(info => info.ConnectionId).ToList();
        foreach (var connectionId in connectionIdsToKick)
        {
            _connectionInfoMap.Remove(connectionId);

            await Groups.RemoveFromGroupAsync(connectionId, roomCode);
            _chatMessage = new ChatMessage
            {
                UserName = userId,
                GroupName = roomCode,
                MessageContent = "Bị đuổi khỏi phòng chat !",
            };
            await Clients.Group(roomCode).SendAsync("ReceiveMessage", _chatMessage);
        }
    }

    #endregion

    #region Ra khỏi phòng chat
    public async Task LeaveRoomAsync(string roomCode)
    {
        // Tìm mã phòng mà người dùng đang tham gia
        if (_connectionInfoMap.TryGetValue(Context.ConnectionId, out var connectionInfo) && connectionInfo.RoomCode == roomCode)
        {
            // Xóa người dùng khỏi nhóm phòng
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);

            _chatMessage = new ChatMessage
            {
                UserName = connectionInfo.UserId,
                GroupName = roomCode,
                MessageContent = "Đã rời phòng chat !",
            };

            await Clients.Group(roomCode).SendAsync("ReceiveMessage", _chatMessage);

            // Xóa thông tin người dùng
            _connectionInfoMap.Remove(Context.ConnectionId);
        }
    }
    #endregion

    #region Gửi tin nhắn đến toàn bộ người trong phòng chat
    public async Task SendMessageToRoomAsync(ChatMessage model)
    {
        // Kiểm tra xem ConnectionId của người gửi tin nhắn có tồn tại trong phòng không
        if (_connectionInfoMap.TryGetValue(Context.ConnectionId, out var connectionInfo) && connectionInfo.RoomCode == model.GroupName)
        {
            var db = _redisConnection.GetDatabase();
            var serializeMessage = JsonSerializer.Serialize(model);
            await db.ListRightPushAsync(model.GroupName, serializeMessage);

            if (await db.ListLengthAsync(model.GroupName) > 30)
            {
                await db.ListLeftPopAsync(model.GroupName);
            }
            _userLastActivity[Context.ConnectionId] = DateTime.UtcNow;
            // Gửi tin nhắn đến tất cả người trong phòng
            await Clients.Group(model.GroupName).SendAsync("ReceiveMessage", model);
        }
        else
        {
            await Clients.Caller.SendAsync("ReceiveMessage", new ChatMessage
            {
                UserName = "Hệ thống",
                GroupName = model.GroupName,
                MessageContent = "Bạn đã bị mất quyền phát ngôn tại phòng chat !"
            });
        }
    }
    #endregion

    #region Thực hiện mở tab người dùng khi tồn tại Room
    public async Task OpenTabsForUserAsync(string userId)
    {
        var roomCodes = _connectionInfoMap.Values
            .Where(info => info.UserId == userId)
            .Select(info => info.RoomCode)
            .Distinct()
            .ToList();

        if (roomCodes.Count() > 0)
        {
            await Clients.Caller.SendAsync("openTabsForUser", userId, roomCodes);
        }
    }
    #endregion

    #region Thực hiện cập nhật khi tin nhắn bị xoá
    public async Task DeleteMessageAsync(Guid messageId, string roomCode, string idUser)
    {
        if (_connectionInfoMap.TryGetValue(Context.ConnectionId, out var connectionInfo) && connectionInfo.UserId == idUser)
        {
            await Clients.Group(roomCode).SendAsync("MessageDeleted", messageId);
        }
    }
    #endregion

    #region Giải phóng khi người dùng mất kết nối
    public override async Task OnDisconnectedAsync(Exception exception)
    {
        if (_connectionInfoMap.TryGetValue(Context.ConnectionId, out var connectionInfo))
        {

            _chatMessage = new ChatMessage
            {
                UserName = connectionInfo.UserId,
                GroupName = connectionInfo.RoomCode,
                MessageContent = "Đã bị mất kết nối !",
            };
            await Clients.Group(connectionInfo.RoomCode).SendAsync("ReceiveMessage", _chatMessage);

            if (_roomOccupancy.ContainsKey(connectionInfo.RoomCode))
            {
                _roomOccupancy[connectionInfo.RoomCode]--;
                if (_roomOccupancy[connectionInfo.RoomCode] <= 0)
                {
                    _roomOccupancy.Remove(connectionInfo.RoomCode);
                }
            }
            _connectionInfoMap.Remove(Context.ConnectionId);
        }

        _pingTimer?.Stop();
        _pingTimer?.Dispose();
        Context.Abort();
        await base.OnDisconnectedAsync(exception);
    }

    #endregion

}



