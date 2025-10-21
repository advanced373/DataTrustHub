using DataTrustHub.SharedKernel;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataTrustHub.Application.Abstractions.Messaging
{
#pragma warning disable CA1040 // Avoid empty interfaces
    public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
#pragma warning restore CA1040 // Avoid empty interfaces
}
