using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using VenstarTranslator.Controllers;
using VenstarTranslator.Models;
using VenstarTranslator.Services;
using Xunit;

namespace VenstarTranslator.Tests;

public class APIControllerTests : IDisposable
{
    private readonly VenstarTranslatorDataCache _db;
    private readonly Mock<ILogger<API>> _mockLogger;
    private readonly IConfiguration _config;
    private readonly Mock<ISensorOperations> _mockSensorOps;
    private readonly Mock<IHangfireJobManager> _mockJobManager;
    private readonly API _controller;

    public APIControllerTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<VenstarTranslatorDataCache>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new VenstarTranslatorDataCache(options);

        // Setup mocks
        _mockLogger = new Mock<ILogger<API>>();
        _mockSensorOps = new Mock<ISensorOperations>();
        _mockJobManager = new Mock<IHangfireJobManager>();

        // Use real in-memory configuration instead of mocking
        var configDict = new Dictionary<string, string>
        {
            { "SensorFilePath", System.IO.Path.GetTempFileName() }
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict!)
            .Build();

        // Create controller
        _controller = new API(_mockLogger.Object, _db, _config, _mockSensorOps.Object, _mockJobManager.Object);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private TranslatedVenstarSensor CreateTestSensor(byte id = 0, string name = "Test Sensor")
    {
        return new TranslatedVenstarSensor
        {
            SensorID = id,
            Name = name,
            Enabled = true,
            Purpose = SensorPurpose.Remote,
            Scale = TemperatureScale.F,
            URL = "http://example.com/api",
            IgnoreSSLErrors = false,
            JSONPath = "$.temperature",
            Headers = new List<DataSourceHttpHeader>()
        };
    }

    #region GetReading Tests

    [Fact]
    public void GetReading_SensorNotFound_Returns404()
    {
        // Act
        var result = _controller.GetReading(99);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusCodeResult.StatusCode);
    }

    [Fact]
    public void GetReading_Success_ReturnsTemperatureAndScale()
    {
        // Arrange
        var sensor = CreateTestSensor();
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        _mockSensorOps.Setup(s => s.GetLatestReading(It.IsAny<TranslatedVenstarSensor>()))
            .Returns(72.5);

        // Act
        var result = _controller.GetReading(0);

        // Assert
        var jsonResult = Assert.IsType<JsonResult>(result);
        var response = Assert.IsType<TemperatureResponse>(jsonResult.Value);
        Assert.Equal(72.5, response.Temperature);
        Assert.Equal(TemperatureScale.F, response.Scale);
    }

    [Fact]
    public void GetReading_HttpRequestException_Returns400WithMessage()
    {
        // Arrange
        var sensor = CreateTestSensor();
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        _mockSensorOps.Setup(s => s.GetLatestReading(It.IsAny<TranslatedVenstarSensor>()))
            .Throws(new HttpRequestException("Connection refused. The server is not accepting connections."));

        // Act
        var result = _controller.GetReading(0);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Contains("Connection refused", response.Message);
    }

    [Fact]
    public void GetReading_InvalidOperationException_Returns400WithMessage()
    {
        // Arrange
        var sensor = CreateTestSensor();
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        _mockSensorOps.Setup(s => s.GetLatestReading(It.IsAny<TranslatedVenstarSensor>()))
            .Throws(new InvalidOperationException("JSONPath error"));

        // Act
        var result = _controller.GetReading(0);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Equal("JSONPath error", response.Message);
    }

    #endregion

    #region SendPairingPacket Tests

    [Fact]
    public void SendPairingPacket_SensorNotFound_Returns404()
    {
        // Act
        var result = _controller.SendPairingPacket(99);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusCodeResult.StatusCode);
    }

    [Fact]
    public void SendPairingPacket_Success_ReturnsSuccessMessage()
    {
        // Arrange
        var sensor = CreateTestSensor();
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        _mockSensorOps.Setup(s => s.SendPairingPacket(It.IsAny<TranslatedVenstarSensor>()));

        // Act
        var result = _controller.SendPairingPacket(0);

        // Assert
        var jsonResult = Assert.IsType<JsonResult>(result);
        var response = Assert.IsType<MessageResponse>(jsonResult.Value);
        Assert.Equal("Pairing packet sent.", response.Message);
        _mockSensorOps.Verify(s => s.SendPairingPacket(It.IsAny<TranslatedVenstarSensor>()), Times.Once);
    }

    [Fact]
    public void SendPairingPacket_HttpRequestException_Returns400WithMessage()
    {
        // Arrange
        var sensor = CreateTestSensor();
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        _mockSensorOps.Setup(s => s.SendPairingPacket(It.IsAny<TranslatedVenstarSensor>()))
            .Throws(new HttpRequestException("Request timed out after 10 seconds."));

        // Act
        var result = _controller.SendPairingPacket(0);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Contains("timed out", response.Message);
    }

    [Fact]
    public void SendPairingPacket_InvalidOperationException_Returns400WithMessage()
    {
        // Arrange
        var sensor = CreateTestSensor();
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        _mockSensorOps.Setup(s => s.SendPairingPacket(It.IsAny<TranslatedVenstarSensor>()))
            .Throws(new InvalidOperationException("JSONPath extraction failed"));

        // Act
        var result = _controller.SendPairingPacket(0);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Equal("JSONPath extraction failed", response.Message);
    }

    #endregion

    #region ListSensors Tests

    [Fact]
    public void ListSensors_NoSensors_ReturnsEmptyList()
    {
        // Act
        var result = _controller.ListSensors();

        // Assert
        var jsonResult = Assert.IsType<JsonResult>(result);
        var sensors = Assert.IsAssignableFrom<List<TranslatedVenstarSensor>>(jsonResult.Value);
        Assert.Empty(sensors);
    }

    [Fact]
    public void ListSensors_WithSensors_ReturnsSensorList()
    {
        // Arrange
        var sensor1 = CreateTestSensor(0, "Sensor 1");
        var sensor2 = CreateTestSensor(1, "Sensor 2");
        _db.Sensors.AddRange(sensor1, sensor2);
        _db.SaveChanges();

        // Act
        var result = _controller.ListSensors();

        // Assert
        var jsonResult = Assert.IsType<JsonResult>(result);
        var sensors = Assert.IsAssignableFrom<List<TranslatedVenstarSensor>>(jsonResult.Value);
        Assert.Equal(2, sensors.Count);
    }

    #endregion

    #region AddSensor Tests

    [Fact]
    public void AddSensor_ValidSensor_ReturnsSuccess()
    {
        // Arrange
        var sensor = CreateTestSensor(name: "New Sensor");

        // Act
        var result = _controller.AddSensor(sensor);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MessageResponse>(okResult.Value);
        Assert.Equal("Successful!", response.Message);

        // Verify sensor was added
        var addedSensor = _db.Sensors.FirstOrDefault(s => s.Name == "New Sensor");
        Assert.NotNull(addedSensor);
    }

    [Fact]
    public void AddSensor_NoAvailableIDs_Returns400()
    {
        // Arrange - fill all 20 sensor slots
        for (byte i = 0; i < 20; i++)
        {
            _db.Sensors.Add(CreateTestSensor(i, $"Sensor {i}"));
        }
        _db.SaveChanges();

        var newSensor = CreateTestSensor(name: "Overflow Sensor");

        // Act
        var result = _controller.AddSensor(newSensor);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Contains("No sensor IDs available", response.Message);
    }

    [Fact]
    public void AddSensor_AssignsLowestAvailableID()
    {
        // Arrange - add sensors with IDs 0, 1, 3 (skip 2)
        _db.Sensors.Add(CreateTestSensor(0, "Sensor 0"));
        _db.Sensors.Add(CreateTestSensor(1, "Sensor 1"));
        _db.Sensors.Add(CreateTestSensor(3, "Sensor 3"));
        _db.SaveChanges();

        var newSensor = CreateTestSensor(name: "New Sensor");

        // Act
        _controller.AddSensor(newSensor);

        // Assert - should get ID 2
        var addedSensor = _db.Sensors.FirstOrDefault(s => s.Name == "New Sensor");
        Assert.NotNull(addedSensor);
        Assert.Equal(2, addedSensor.SensorID);
    }

    #endregion

    #region UpdateSensor Tests

    [Fact]
    public void UpdateSensor_SensorNotFound_Returns404()
    {
        // Arrange
        var sensor = CreateTestSensor(99);

        // Act
        var result = _controller.UpdateSensor(sensor);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusCodeResult.StatusCode);
    }

    [Fact]
    public void UpdateSensor_ValidUpdate_ReturnsSuccess()
    {
        // Arrange
        var sensor = CreateTestSensor(0, "Original Name");
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        var updatedSensor = CreateTestSensor(0, "Updated Name");
        updatedSensor.URL = "http://newurl.com/api";

        // Act
        var result = _controller.UpdateSensor(updatedSensor);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MessageResponse>(okResult.Value);
        Assert.Equal("Successful!", response.Message);

        // Verify changes
        var dbSensor = _db.Sensors.Find((byte)0);
        Assert.NotNull(dbSensor);
        Assert.Equal("Updated Name", dbSensor.Name);
        Assert.Equal("http://newurl.com/api", dbSensor.URL);
    }

    [Fact]
    public void UpdateSensor_UpdatesHeaders()
    {
        // Arrange
        var sensor = CreateTestSensor(0);
        sensor.Headers.Add(new DataSourceHttpHeader { Name = "Old-Header", Value = "old" });
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        var updatedSensor = CreateTestSensor(0);
        updatedSensor.Headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "New-Header", Value = "new" }
        };

        // Act
        _controller.UpdateSensor(updatedSensor);

        // Assert
        var dbSensor = _db.Sensors.Include(s => s.Headers).First(s => s.SensorID == 0);
        Assert.Single(dbSensor.Headers);
        Assert.Equal("New-Header", dbSensor.Headers[0].Name);
    }

    #endregion

    #region DeleteSensor Tests

    [Fact]
    public void DeleteSensor_SensorNotFound_Returns404()
    {
        // Act
        var result = _controller.DeleteSensor(99);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, statusCodeResult.StatusCode);
    }

    [Fact]
    public void DeleteSensor_ValidSensor_ReturnsSuccess()
    {
        // Arrange
        var sensor = CreateTestSensor(0);
        _db.Sensors.Add(sensor);
        _db.SaveChanges();

        // Act
        var result = _controller.DeleteSensor(0);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MessageResponse>(okResult.Value);
        Assert.Equal("Successful!", response.Message);

        // Verify deletion
        Assert.Null(_db.Sensors.Find((byte)0));
    }

    #endregion

    #region TestJsonPath Tests

    [Fact]
    public void TestJsonPath_MissingDocument_Returns400()
    {
        // Arrange
        var test = new JSONPathTest { Query = "$.test" };

        // Act
        var result = _controller.TestJsonPath(test);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Contains("JSON Document is required", response.Message);
    }

    [Fact]
    public void TestJsonPath_MissingQuery_Returns400()
    {
        // Arrange
        var test = new JSONPathTest { JSONDocument = "{\"test\": 123}" };

        // Act
        var result = _controller.TestJsonPath(test);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Contains("JSON Path Query is required", response.Message);
    }

    [Fact]
    public void TestJsonPath_NoResults_ReturnsMessage()
    {
        // Arrange
        var test = new JSONPathTest
        {
            JSONDocument = "{\"temperature\": 72.5}",
            Query = "$.nonexistent"
        };

        // Act
        var result = _controller.TestJsonPath(test);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MessageResponse>(okResult.Value);
        Assert.Equal("No results found.", response.Message);
    }

    [Fact]
    public void TestJsonPath_SingleResult_ReturnsValue()
    {
        // Arrange
        var test = new JSONPathTest
        {
            JSONDocument = "{\"temperature\": 72.5}",
            Query = "$.temperature"
        };

        // Act
        var result = _controller.TestJsonPath(test);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MessageResponse>(okResult.Value);
        Assert.Contains("72.5", response.Message);
    }

    [Fact]
    public void TestJsonPath_MultipleResults_ReturnsWarning()
    {
        // Arrange
        var test = new JSONPathTest
        {
            JSONDocument = "{\"sensors\": [{\"temp\": 72}, {\"temp\": 75}]}",
            Query = "$.sensors[*].temp"
        };

        // Act
        var result = _controller.TestJsonPath(test);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<MessageResponse>(okResult.Value);
        Assert.Contains("Multiple results found", response.Message);
    }

    [Fact]
    public void TestJsonPath_InvalidJson_Returns400()
    {
        // Arrange
        var test = new JSONPathTest
        {
            JSONDocument = "not valid json",
            Query = "$.test"
        };

        // Act
        var result = _controller.TestJsonPath(test);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Contains("JSON Document Error", response.Message);
    }

    [Fact]
    public void TestJsonPath_InvalidJsonPath_Returns400()
    {
        // Arrange
        var test = new JSONPathTest
        {
            JSONDocument = "{\"temperature\": 72.5}",
            Query = "$.[invalid"
        };

        // Act
        var result = _controller.TestJsonPath(test);

        // Assert
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, statusCodeResult.StatusCode);
        var response = Assert.IsType<MessageResponse>(statusCodeResult.Value);
        Assert.Contains("JSON Path Error", response.Message);
    }

    #endregion
}
