namespace DataTrustHub.Services.Api;

public interface IDataApiClient
{
    // policyId and levelPriority classify the file; both null keeps it personal
    Task<ApiResult<Guid>> UploadAsync(
        IFormFile file, Guid? policyId, int? levelPriority, CancellationToken cancellationToken);

    Task<ApiResult<ClassificationOptionsResponse>> GetClassificationOptionsAsync(CancellationToken cancellationToken);

    // Own items plus those the user's clearances allow
    Task<ApiResult<IReadOnlyList<DataItemResponse>>> ListAsync(CancellationToken cancellationToken);

    Task<ApiResult<DataItemContentResponse>> GetContentAsync(Guid id, CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class DataApiClient(HttpClient httpClient) : IDataApiClient
{
    public async Task<ApiResult<Guid>> UploadAsync(
        IFormFile file, Guid? policyId, int? levelPriority, CancellationToken cancellationToken)
    {
        await using var fileStream = file.OpenReadStream();
        using var content = new MultipartFormDataContent
        {
            { new StreamContent(fileStream), Constants.UploadFormField, file.FileName }
        };
        if (policyId is not null && levelPriority is not null)
        {
            content.Add(new StringContent(policyId.Value.ToString()), Constants.PolicyFormField);
            content.Add(new StringContent(levelPriority.Value.ToString()), Constants.LevelFormField);
        }

        var result = await ApiRequest.SendAsync<DataItemCreatedResponse>(
            token => httpClient.PostAsync(ApiRoutes.UploadData, content, token),
            cancellationToken);

        return result.Map(response => response.DataItemId);
    }

    public Task<ApiResult<ClassificationOptionsResponse>> GetClassificationOptionsAsync(
        CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<ClassificationOptionsResponse>(
            token => httpClient.GetAsync(ApiRoutes.ClassificationOptions, token), cancellationToken);

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
