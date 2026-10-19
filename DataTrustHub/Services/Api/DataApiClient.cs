namespace DataTrustHub.Services.Api;

public interface IDataApiClient
{
    Task<ApiResult<Guid>> UploadAsync(IFormFile file, CancellationToken cancellationToken);
}

internal sealed class DataApiClient(HttpClient httpClient) : IDataApiClient
{
    public async Task<ApiResult<Guid>> UploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var fileStream = file.OpenReadStream();
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(fileStream), Constants.UploadFormField, file.FileName }
        };

        var result = await ApiRequest.SendAsync<DataItemCreatedResponse>(
            token => httpClient.PostAsync(ApiRoutes.UploadData, content, token),
            cancellationToken);

        return result.Map(response => response.DataItemId);
    }
}
