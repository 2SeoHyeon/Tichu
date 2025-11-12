using Microsoft.EntityFrameworkCore;
using Tichu.Hub;
using Tichu.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

//builder.Services.AddScoped<RoomService>();
//builder.Services.AddScoped<GameService>();
//builder.Services.AddScoped<TimerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Lobby}/{action=Index}/{id?}");

app.MapHub<TichuHub>("/tichuHub");

app.Run();
