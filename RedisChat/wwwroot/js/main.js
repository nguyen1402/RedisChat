// main.js

// Sử dụng require để tải thư viện ioredis
const Redis = require('ioredis');

// Tạo kết nối Redis
const redis = new Redis({
    host: 'redis-16227.c278.us-east-1-4.ec2.cloud.redislabs.com',
    port: 16227,
    password: 'SlJ3AuLw1i6SuLSj3restUqag7kp2SXq'
});

// Đăng ký lắng nghe tin nhắn
var channelName = "@ViewBag.GroupName";

redis.subscribe(channelName, function (err, count) {
    if (err) {
        console.error(err);
    }

    redis.on('message', function (channel, message) {
        var messageData = JSON.parse(message);
        var messageDiv = document.createElement("div");
        messageDiv.className = "message";
        messageDiv.innerHTML = `<p><strong>${messageData.userName}</strong> (${new Date(messageData.timeStamp).toLocaleTimeString("en-US", { hour: 'numeric', minute: 'numeric', hour12: true })})</p>
                                                                <p>${messageData.messageContent}</p>`;
        document.getElementById("chat-messages").appendChild(messageDiv);
    });
});



