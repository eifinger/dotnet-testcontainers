# Testcontainers Demo

Demo Application which shows how to use a real database during integrationtests for C# Applications.

This Application uses

* ASP.NET Core
* EF Core
* [PostgreSQL](https://hub.docker.com/_/postgres)
* [Alba](https://jasperfx.github.io/alba/guide/)
* [Testcontainer](https://www.testcontainers.org/)
* xUnit

## What are Testcontainers?

Testcontainers is a Java library that supports JUnit tests, providing lightweight, throwaway instances of common databases, Selenium web browsers, or anything else that can run in a Docker container.

https://github.com/testcontainers/testcontainers-dotnet is a dotnet version of it.

It enables you to automatically spin up any docker container e.g. a postgreSQL DB prepare it for tests and automatically destroy it after your tests ran.

This way you can run your integrationtests against a real db without using in-memory subsitutes or manually setting up a database before running your services.

## How to set up the project

### Clone this repo

You can just clone this repo and run the unit tests.

### From scratch

#### Create the projects

Create a new directory `dotnet-testcontainers` by running

````shell
mkdir dotnet-testcontainers
````

Change into the directory

````shell
cd dotnet-testcontainers
````

Initialize a solution and add a ASP.NET Core API project with the name `Application` to it

````shell
dotnet new sln
dotnet new webapi -o Application
dotnet sln add ./Application/Application.csproj
````

Add a xUnit Project with the name `Application.Tests` to it

````shell
dotnet new xunit -o Application.Tests
dotnet add .\Application.Tests\Application.Tests.csproj reference .\Application\Application.csproj
dotnet sln add .\Application.Tests\Application.Tests.csproj
````

#### Add dependencies

Add needed libraries to our xUnit Project

````shell
cd .\Application.Tests
dotnet add package Alba --version 6.1.0
dotnet add package Testcontainers --version 2.2.0-beta.2835065501
dotnet add package FluentAssertions --version 6.7.0
````

Change to the `Application` project directory and add additional libraries.

* PostgreSQL Provider for EF Core
* Codegenerators for REST Controllers

````shell
 cd ..\Application
 dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 6.0.7
 dotnet add package Microsoft.VisualStudio.Web.CodeGeneration.Design
 dotnet add package Microsoft.EntityFrameworkCore.Design
 dotnet add package Microsoft.EntityFrameworkCore.SqlServer
````

#### Additional Code

From here on open the solution with an IDE. I am using Rider.

Adapted from [this tutorial](https://learn.microsoft.com/en-us/aspnet/core/tutorials/first-web-api?view=aspnetcore-6.0&tabs=visual-studio-code) we will create a ToDo Application.

The webapi template sets up a REST api which provides Information as defined in `WeatherForecast.cs`. We will add an additional REST api to create, read and delete ToDo items.

#### Model

Create a new folder `Models` and in it create a class `TodoItem` with the following content.

````csharp
public class TodoItem
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public bool IsComplete { get; set; }
}
````

Also in the `Models` folder create a class `TodoContext` for EF core with the following content.

````csharp
public class TodoContext : DbContext
{
    public TodoContext(DbContextOptions<TodoContext> options)
        : base(options)
    {
    }

    public DbSet<TodoItem> TodoItems { get; set; } = null!;
````

#### Register the database context

We now have the model to work with. The next step is to configure the database. In `Program.cs` add the following line:

````csharp
builder.Services.AddDbContext<TodoContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("TodoContext")));
````

and in `appsettings.Development.json` add a connection string so it looks like this:

````csharp
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "TodoContext": "Host=localhost;Database=postgres;Username=postgres;Password=postgres"
  }
}
````

#### Generate controller and migration

In the next step we use a code generator utility to quickly create the code to handle CRUD operations via REST.

````csharp
dotnet tool install -g dotnet-aspnet-codegenerator
dotnet-aspnet-codegenerator controller -name TodoItemsController -async -api -m TodoItem -dc TodoContext -outDir Controllers
dotnet ef migrations add InitialCreate
````

This will create `TodoItemsController.cs` in the `Models` folder and add an EF core migration to create the needed table structure.

#### Try out the application

If we want to give the api a test run by hand with the swagger ui or Postman we have to create a database and initialize it. We can do that by starting a docker container and running the EF core migration.

````csharp
docker run --name testcontainers-demo -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres
dotnet ef database update
````

When we now start the application we should see the swagger ui and can create and get TodoItems.

#### Create a simple xUnit test with Alba

To automatically test the functionality we will use xUnit tests.
We first have to make the `Application` project visible to the `Application.Test` project so `Alba` can find the .NET 6 [WebApplicationBuilder](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.webapplicationbuilder?view=aspnetcore-6.0).

Open the `Application.csproj` file and add the following to it:

````csharp
<ItemGroup>
    <InternalsVisibleTo Include="Application.Tests" />
</ItemGroup>
````

Now go into the `Application.Tests` project and open `UnitTest1.cs`. Replace the code of the first unit test so it looks like this:

````csharp
[Fact]
public async void Test1()
{
    await using var host = await AlbaHost.For<Program>(x => { x.ConfigureServices((context, services) => { }); });

    var result = await host.GetAsJson<List<WeatherForecast>>("/WeatherForecast");

    result.Should().HaveCount(5);
}
````

As you can see we are using Alba to start the whole Application project in memory and communicate with it using some helper methods. The test shows that the normal WeatherForecasts API which does not use any database is working.

#### Add testcontainers

To test the TodoApi we need to have a running database. We will use testcontainers to provide us with a fresh instance.

Let the `UnitTest1.cs` class extend `IAsyncLifetime` and add the following code to it. This will create a postgres container, start it before any test methods run and remove it again if all tests are finished running or are aborted.

````csharp
private readonly TestcontainerDatabase Testcontainer = new TestcontainersBuilder<PostgreSqlTestcontainer>()
    .WithDatabase(new PostgreSqlTestcontainerConfiguration
    {
        Database = "postgres",
        Username = "postgres",
        Password = "Password12!",
    })
    .Build();

public async Task InitializeAsync()
{
    await Testcontainer.StartAsync();
}

public Task DisposeAsync()
{
    return Testcontainer.DisposeAsync().AsTask();
}
````

Now add a new test to test the ToDoItem API. Here we are overwriting the ConnectionString for the database using the value of the testcontainer. The database will be completely fresh so we will have to run the migrations on it we created earlier. We will then use the same Alba helper methods as in Test1.

````csharp
[Fact]
    public async void Test2()
    {
        await using var host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseSetting("ConnectionStrings:TodoContext", Testcontainer.ConnectionString);
            builder.ConfigureServices((_, services) =>
                services.BuildServiceProvider().GetService<TodoContext>()?.Database.Migrate());
        });

        await host.Scenario(_ =>
        {
            _.StatusCodeShouldBe(201);
            _.Post
                .Json(new TodoItem
                {
                    Id = 1L,
                    Name = "TestName",
                    IsComplete = false
                })
                .ToUrl("/api/TodoItems");
        });

        var result = await host.GetAsJson<TodoItem>("/api/TodoItems/1");

        result?.Name.Should().Be("TestName");
    }
````

We now have a running Todo API which we can automatically test end to end locally as well as in a CI pipeline without needing any external dependencies or mocks.
