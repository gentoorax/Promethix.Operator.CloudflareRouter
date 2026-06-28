using System.Net;
using System.Text.Json;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

namespace Promethix.CloudflareTunnelOperator.Hosting.Admission;

internal sealed class TunnelPublicHostnameAdmissionService(
    IManagedTunnelPublicHostnameValidator managedValidator)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AdmissionReview> ValidateAsync(AdmissionReview review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);

        var request = review.Request;
        if (request is null)
        {
            return CreateDeniedResponse(string.Empty, "AdmissionReview.request is required.", HttpStatusCode.BadRequest);
        }

        if (string.Equals(request.Operation, "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            return CreateAllowedResponse(request.Uid);
        }

        if (request.Object.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return CreateDeniedResponse(request.Uid, "AdmissionReview.request.object is required.", HttpStatusCode.BadRequest);
        }

        TunnelPublicHostnameCustomResource? resource;
        try
        {
            resource = request.Object.Deserialize<TunnelPublicHostnameCustomResource>(JsonOptions);
        }
        catch (JsonException ex)
        {
            return CreateDeniedResponse(request.Uid, $"TunnelPublicHostname payload could not be parsed: {ex.Message}", HttpStatusCode.BadRequest);
        }

        if (resource is null)
        {
            return CreateDeniedResponse(request.Uid, "TunnelPublicHostname payload could not be parsed.", HttpStatusCode.BadRequest);
        }

        if (!managedValidator.IsManaged(resource))
        {
            return CreateAllowedResponse(request.Uid);
        }

        try
        {
            await managedValidator.ValidateAsync(resource, cancellationToken).ConfigureAwait(false);
            return CreateAllowedResponse(request.Uid);
        }
        catch (ArgumentException ex)
        {
            return CreateDeniedResponse(request.Uid, ex.Message, HttpStatusCode.UnprocessableEntity);
        }
        catch (InvalidOperationException ex)
        {
            return CreateDeniedResponse(request.Uid, ex.Message, HttpStatusCode.UnprocessableEntity);
        }
        catch (UriFormatException ex)
        {
            return CreateDeniedResponse(request.Uid, ex.Message, HttpStatusCode.UnprocessableEntity);
        }
    }

    private static AdmissionReview CreateAllowedResponse(string uid)
    {
        return new AdmissionReview
        {
            Response = new AdmissionResponse
            {
                Uid = uid,
                Allowed = true,
            },
        };
    }

    private static AdmissionReview CreateDeniedResponse(string uid, string message, HttpStatusCode statusCode)
    {
        return new AdmissionReview
        {
            Response = new AdmissionResponse
            {
                Uid = uid,
                Allowed = false,
                Status = new AdmissionStatus
                {
                    Message = message,
                    Code = (int)statusCode,
                },
            },
        };
    }
}
