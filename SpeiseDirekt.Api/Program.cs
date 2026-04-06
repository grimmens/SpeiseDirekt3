using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Database
var connectionString = builder.Configuration.GetConnectionString("server")
    ?? throw new InvalidOperationException("Connection string 'server' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
});

// Identity API endpoints
builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Application services
builder.Services.ConfigureServices();

// Controllers, Swagger, Auth
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapIdentityApi<ApplicationUser>();

app.Run();

public partial class Program { }
