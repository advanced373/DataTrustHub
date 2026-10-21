namespace DataTrustHub.Services.Api;

public interface IDataApiClient
{
    Task<ApiResult<Guid>> UploadAsync(IFormFile file, CancellationToken cancellationToken);

    // Own items plus those the user's clearances allow
    Task<ApiResult<IReadOnlyList<DataItemResponse>>> ListAsync(CancellationToken cancellationToken);

    Task<ApiResult<DataItemContentResponse>> GetContentAsync(Guid id, CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
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

    public Task<ApiResult<IReadOnlyList<DataItemResponse>>> ListAsync(CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<IReadOnlyList<DataItemResponse>>(
            token => httpClient.GetAsync(ApiRoutes.DataItems, token), cancellationToken);

    public Task<ApiResult<DataItemContentResponse>> GetContentAsync(Guid id, CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<DataItemContentResponse>(
            token => httpClient.GetAsync(string.Format(ApiRoutes.DataItemById, id), token), cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        ApiRequest.SendWithoutContentAsync(
            token => httpClient.DeleteAsync(string.Format(ApiRoutes.DataItemById, id), token), cancellationToken);
}
