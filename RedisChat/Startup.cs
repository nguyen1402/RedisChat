using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RedisChat.Models;
using StackExchange.Redis;
using System;

namespace RedisChat
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect("redis-16227.c278.us-east-1-4.ec2.cloud.redislabs.com:16227,password=SlJ3AuLw1i6SuLSj3restUqag7kp2SXq"));
            services.AddSingleton<ChatHub>();
            services.AddDistributedMemoryCache(); // Thêm cache phân phối (có thể thay thế bằng cache khác)
            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(60); // Đặt thời gian tồn tại của phiên làm việc
                options.Cookie.HttpOnly = true;
            });

            services.AddHttpContextAccessor();
            services.AddSignalR();

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseSession();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Chat}/{action=JoinOrCreateGroupChat}/{id?}");
                endpoints.MapHub<ChatHub>("chatHub");
            });
        }
    }
}
