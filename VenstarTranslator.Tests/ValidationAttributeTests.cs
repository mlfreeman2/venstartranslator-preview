using System.Collections.Generic;
using VenstarTranslator.Models.Db;
using VenstarTranslator.Models.Validation;
using Xunit;

namespace VenstarTranslator.Tests;

public class ValidationAttributeTests
{
    #region ValidAbsoluteUrlAttribute Tests

    [Fact]
    public void ValidAbsoluteUrl_ValidHttpUrl_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("http://example.com/api");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidAbsoluteUrl_ValidHttpsUrl_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("https://example.com/api/endpoint");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidAbsoluteUrl_UrlWithQueryString_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("http://example.com/api?param=value&other=123");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidAbsoluteUrl_UrlWithPort_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("http://192.168.1.100:8080/api");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidAbsoluteUrl_NullValue_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid(null);

        // Assert
        Assert.True(result); // Lets Required attribute handle nulls
    }

    [Fact]
    public void ValidAbsoluteUrl_EmptyString_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("");

        // Assert
        Assert.True(result); // Lets Required attribute handle empty
    }

    [Fact]
    public void ValidAbsoluteUrl_WhitespaceOnly_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("   ");

        // Assert
        Assert.True(result); // Lets Required attribute handle whitespace
    }

    [Fact]
    public void ValidAbsoluteUrl_RelativeUrl_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("/api/endpoint");

        // Assert
        Assert.False(result);
        Assert.Equal("The URL must be a properly formed absolute URL.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidAbsoluteUrl_MissingScheme_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("example.com/api");

        // Assert
        Assert.False(result);
        Assert.Equal("The URL must be a properly formed absolute URL.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidAbsoluteUrl_InvalidUrl_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidAbsoluteUrlAttribute();

        // Act
        var result = attribute.IsValid("not a valid url");

        // Assert
        Assert.False(result);
        Assert.Equal("The URL must be a properly formed absolute URL.", attribute.ErrorMessage);
    }

    #endregion

    #region ValidHttpHeadersAttribute Tests

    [Fact]
    public void ValidHttpHeaders_NullValue_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();

        // Act
        var result = attribute.IsValid(null);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidHttpHeaders_EmptyList_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>();

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidHttpHeaders_ValidSingleHeader_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = "Bearer token123" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidHttpHeaders_ValidMultipleHeaders_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = "Bearer token123" },
            new DataSourceHttpHeader { Name = "X-Custom-Header", Value = "custom-value" },
            new DataSourceHttpHeader { Name = "Accept", Value = "application/json" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidHttpHeaders_NullHeaderName_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = null, Value = "some-value" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain entries with null, blank, or white space names or values.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidHttpHeaders_EmptyHeaderName_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "", Value = "some-value" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain entries with null, blank, or white space names or values.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidHttpHeaders_WhitespaceHeaderName_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "   ", Value = "some-value" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain entries with null, blank, or white space names or values.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidHttpHeaders_NullHeaderValue_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = null }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain entries with null, blank, or white space names or values.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidHttpHeaders_EmptyHeaderValue_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = "" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain entries with null, blank, or white space names or values.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidHttpHeaders_WhitespaceHeaderValue_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = "   " }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain entries with null, blank, or white space names or values.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidHttpHeaders_DuplicateHeaderNames_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = "Bearer token1" },
            new DataSourceHttpHeader { Name = "Authorization", Value = "Bearer token2" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain duplicate header names.", attribute.ErrorMessage);
    }

    [Fact]
    public void ValidHttpHeaders_DuplicateAmongMultipleHeaders_ReturnsFalseWithMessage()
    {
        // Arrange
        var attribute = new ValidHttpHeadersAttribute();
        var headers = new List<DataSourceHttpHeader>
        {
            new DataSourceHttpHeader { Name = "Authorization", Value = "Bearer token123" },
            new DataSourceHttpHeader { Name = "X-Custom", Value = "custom-value" },
            new DataSourceHttpHeader { Name = "Authorization", Value = "Bearer other-token" }
        };

        // Act
        var result = attribute.IsValid(headers);

        // Assert
        Assert.False(result);
        Assert.Equal("HTTP headers cannot contain duplicate header names.", attribute.ErrorMessage);
    }

    #endregion

    #region ValidJsonPathAttribute Tests

    [Fact]
    public void ValidJsonPath_NullValue_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid(null);

        // Assert
        Assert.True(result); // Lets Required attribute handle nulls
    }

    [Fact]
    public void ValidJsonPath_EmptyString_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("");

        // Assert
        Assert.True(result); // Lets Required attribute handle empty
    }

    [Fact]
    public void ValidJsonPath_WhitespaceOnly_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("   ");

        // Assert
        Assert.True(result); // Lets Required attribute handle whitespace
    }

    [Fact]
    public void ValidJsonPath_SimplePropertyPath_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("temperature");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidJsonPath_NestedPropertyPath_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("data.temperature");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidJsonPath_ArrayIndexPath_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("sensors[0].temperature");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidJsonPath_FilterExpression_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("sensors[?(@.name=='outdoor')].temperature");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidJsonPath_RecursiveDescent_ReturnsTrue()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("$..temperature");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidJsonPath_InvalidSyntaxWithDoubleQuotes_ReturnsFalse()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("sensors[?(@.name==\"outdoor\")].temperature");

        // Assert
        Assert.False(result);
        Assert.NotNull(attribute.ErrorMessage);
    }

    [Fact]
    public void ValidJsonPath_InvalidSyntax_ReturnsFalse()
    {
        // Arrange
        var attribute = new ValidJsonPathAttribute();

        // Act
        var result = attribute.IsValid("sensors[invalid].temperature");

        // Assert
        Assert.False(result);
        Assert.NotNull(attribute.ErrorMessage);
    }

    #endregion
}
