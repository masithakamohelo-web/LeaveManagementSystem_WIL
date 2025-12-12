using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Leave2Day.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add EmailService config from appsettings.json (make sure you have EmailSettings section)
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// Register EmailService (no HttpClient needed here since MailKit handles SMTP)
builder.Services.AddTransient<EmailService>();

// Register FirestoreService as IFirebaseService (your implementation)
builder.Services.AddScoped<IFirebaseService, FirestoreService>();

// Firebase initialization as singleton so it is created once
builder.Services.AddSingleton<FirebaseApp>(serviceProvider =>
{
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    var credentialPath = config["Google:CredentialPath"];
    var projectId = config["Google:ProjectId"];

    var options = new AppOptions
    {
        Credential = GoogleCredential.FromFile(credentialPath),
        ProjectId = projectId
    };
    return FirebaseApp.Create(options);
});

// Add controllers with views
builder.Services.AddControllersWithViews();

// Configure authentication cookie
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
    });

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
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
