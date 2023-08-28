using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RedisChat.Models
{
    public class RedisHubs : Hub
    {
        public event Action<ChatMessage> OnNewMessage;
        private readonly IConnectionMultiplexer _redisConnection;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _groupConnections = new ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>();

        private readonly ConcurrentDictionary<string, Timer> _userTimeouts = new ConcurrentDictionary<string, Timer>();
        private static readonly TimeSpan TimeoutDuration = TimeSpan.FromMinutes(60);

        public RedisHubs(IConnectionMultiplexer redisConnection)
        {
            _redisConnection = redisConnection;
        }
        private async Task SubscribeToChannelAsync(string channelName)
        {
            var sub = _redisConnection.GetSubscriber();
            sub.Subscribe(channelName, async (channel, value) =>
            {
                var newMessageString = (string)value;
                var newChatMessage = JsonSerializer.Deserialize<ChatMessage>(newMessageString);
                await SendNewMessageToGroup(newChatMessage);
            });
            
        }

        private async Task SendNewMessageToGroup(ChatMessage newChatMessage)
        { 
            await Clients.Group(newChatMessage.GroupName).SendAsync("ReceiveMessage", newChatMessage); 
        }


            public async Task SendMessage(ChatMessage message)
        {
            var db = _redisConnection.GetDatabase();
            var serializeMessage = JsonSerializer.Serialize(message);
            await db.ListRightPushAsync(message.GroupName, serializeMessage);

            if (await db.ListLengthAsync(message.GroupName) > 30)
            {
                await db.ListLeftPopAsync(message.GroupName);
            }
            // Gửi tin nhắn mới đến các Subscriber thông qua Redis Pub/Sub
            var pubSub = _redisConnection.GetSubscriber();
            await pubSub.PublishAsync(message.GroupName, serializeMessage);
            await SubscribeToChannelAsync(message.GroupName);
            ResetUserTimeout(Context.ConnectionId);
        }

        public async Task JoinGroup(string groupName)
        {
            //Check connectionId tồn tại
            var connectionId = Context.ConnectionId;
            var connections = _groupConnections.GetOrAdd(groupName, new ConcurrentDictionary<string, bool>());
            connections.TryAdd(connectionId, true);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            await Clients.Caller.SendAsync("UpdateConnectionStatus", true);
            ResetUserTimeout(Context.ConnectionId);
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            //ResetUserTimeout(Context.ConnectionId);
        }
        private void ResetUserTimeout(string connectionId)
        {
            //_userTimeouts.TryGetValue(connectionId, out var timer);
            //timer?.Change(TimeoutDuration, Timeout.InfiniteTimeSpan); 

            _userTimeouts.TryGetValue(connectionId, out var timer);
            // Hủy timer hiện tại nếu có
            timer?.Dispose();
            // Tạo và thiết lập lại timer cho
            //CheckAndLeaveGroupForUser sau thời gian TimeoutDuration
            var newTimer = new Timer(async (state) =>
            {
                await CheckAndLeaveGroupForUser(connectionId);
            }, null, TimeoutDuration, Timeout.InfiniteTimeSpan);
            // Lưu trữ timer mới
            _userTimeouts[connectionId] = newTimer;
        }

        private async Task CheckAndLeaveGroupForUser(string connectionId)
        {
            var groupName = _groupConnections.FirstOrDefault(pair => pair.Value.ContainsKey(connectionId)).Key;

            if (!string.IsNullOrEmpty(groupName))
            {
                var connections = _groupConnections[groupName];
                if (connections.ContainsKey(connectionId))
                {
                    connections.TryRemove(connectionId, out _);
                    await Groups.RemoveFromGroupAsync(connectionId, groupName);
                    await Clients.Caller.SendAsync("UpdateConnectionStatus", false);
                }
                else
                {
                    await Clients.Caller.SendAsync("UpdateConnectionStatus", true);
                }
            }
        }
    }
}



