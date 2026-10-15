using MedicalRecordAnalysis.Api.Cases;

namespace MedicalRecordAnalysis.Api.Endpoints;

/**
 * The firm's profile: its name, address, phone, email and attorney, entered once and used in every case's letter.
 */
public static class FirmEndpoints
{
    private const int MaxDetailLength = 300;

    public static void MapFirmEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder firm = app.MapGroup("/api/firm");
        firm.MapGet("/", GetProfile);
        firm.MapPut("/", UpdateProfile);
    }

    private static async Task<IResult> GetProfile(ICaseStore store, CancellationToken cancellationToken)
    {
        return Results.Ok(await store.LoadFirmProfile(cancellationToken));
    }

    private static async Task<IResult> UpdateProfile(FirmProfile request, ICaseStore store, CancellationToken cancellationToken)
    {
        string?[] details = [request.FirmName, request.Address, request.Phone, request.Email, request.AttorneyName];
        string?[] normalized = new string?[details.Length];
        for (int i = 0; i < details.Length; i++)
        {
            (string? value, string? error) = CaseEndpoints.NormalizeLetterValue(details[i], MaxDetailLength);
            if (error is not null)
            {
                return Results.Json(new ErrorResponse(error), statusCode: StatusCodes.Status400BadRequest);
            }

            normalized[i] = value;
        }

        FirmProfile profile = new FirmProfile(normalized[0], normalized[1], normalized[2], normalized[3], normalized[4]);
        await store.SaveFirmProfile(profile, cancellationToken);
        return Results.Ok(profile);
    }
}
