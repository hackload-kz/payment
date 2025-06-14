// SPDX-FileCopyrightText: 2025 - HackLoad 2025 team
//
// SPDX-License-Identifier: MIT
open Payment.Core
open System
open System.Security.Claims
open System.Text
open System.Threading.Tasks
open System.Text.Encodings.Web
open Oxpecker
open Oxpecker.OpenApi
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Microsoft.OpenApi.Models
open type Microsoft.AspNetCore.Http.TypedResults
open Microsoft.AspNetCore.Authorization
open Payment.Templates
open Payment.Tools

let defaultMerchants = [|
    { merchant_id = "grmklt123"; merchant_key = "1234567890" };
    { merchant_id = "hxeqnd852"; merchant_key = "1234567890" };
    { merchant_id = "merchant_id"; merchant_key = "1234567890" };
|]
merchants <- defaultMerchants

let tran_id = accept_payment_intent "merchant_id"  (getDate()) {
    amount = 100_000L
    currency = "KZT"
    order_id = "order_id"
    description = "description"
    payment_type = "payment_type"
    user_id = "user_id"
    email = "email"
    phone = "phone"
    success_url = "success_url"
    failure_url = "failure_url"
    callback_url = "callback_url"
    payment_lifetime = 1000L
    lang = "lang"
    metadata = Map.empty
}
match tran_id with
| Ok tran_id ->
    accept_card tran_id (getDate()) {
        card_number = "1234567890123456"
        card_expiry = "12/23"
        card_cvc = "123"
        card_holder_name = "John Doe"
    } |> ignore
    confirm_transaction tran_id |> ignore
| Error e ->
    printf "%A" e

type ErrorResponse = 
    { 
        Code: string
    }

type PaymentIntentResult = 
    { 
        transaction_id: string
        result_url: string
    }

let explainErrorCode code =
    match code with
    | InvalidCardNumber -> "invalid_card_info"
    | InvalidCardExpiry -> "invalid_card_info"
    | InvalidCardCVC -> "invalid_card_info"
    | InvalidCardHolderName -> "invalid_card_info"
    | TransactionNotFound -> "transaction_not_found"
    | MerchantNotFound -> "merchant_not_found"
    | PaymentCreationError -> "payment_creation_error"
    | CardAlreadyAccepted -> "card_already_accepted"
    | TransactionNoLongerAvailable -> "transaction_not_available"
    | InvalidCardInformation -> "invalid_card_info"

let handleResultReponse (ctx: HttpContext) result handler =
    match result with
    | Ok transaction_id ->
        ctx.SetStatusCode StatusCodes.Status200OK
        ctx.Write <| Ok (handler transaction_id)
    | Error e ->
        ctx.Write <| BadRequest { Code = explainErrorCode e }

let createPaymentIntent : EndpointHandler =
    fun (ctx: HttpContext) ->
        task {
            let merchant_id = ctx.User.Identity.Name
            let! intent = ctx.BindJson<PaymentIntent>()
            let result = accept_payment_intent merchant_id (getDate()) intent
            return! handleResultReponse ctx result (fun transaction_id -> { transaction_id = transaction_id; result_url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/transaction/{transaction_id}/start" })
        }

let acceptCreditCard id : EndpointHandler =
    fun ctx ->
        let transaction = storage.findTransaction id
        let view = 
            match transaction with
            | Some transaction ->
                AcceptCard.html ""
            | _ -> 
                AcceptCard.invalid_transaction id
        ctx |> writeHtml view

let endpoints = [
    POST [
        route "/payment-intent" createPaymentIntent
            |> addOpenApi (OpenApiConfig(
                requestBody = RequestBody(typeof<PaymentIntent>),
                responseBodies = [| 
                    ResponseBody(typeof<PaymentIntentResult>, statusCode = 200)
                    ResponseBody(typeof<ErrorResponse>, statusCode = 400)
                |],
                configureOperation = (fun o -> o.OperationId <- "CreatePaymentIntent"; o)
            ))
    ] |> configureEndpoint _.RequireAuthorization(
            AuthorizeAttribute(AuthenticationSchemes = "BasicAuthentication")
        )
    // addOpenApiSimple is a shortcut for simple cases
    //GET [
    //    routef "/product/{%i}" (
    //        fun id ->
    //            products
    //            |> Array.find (fun f -> f.Id = num)
    //            |> json
    //    )
    //        |> configureEndpoint _.WithName("GetProduct")
    //        |> addOpenApiSimple<int, Product>
    //]
    GET [
        routef "/transaction/{%s}/start" acceptCreditCard
    ]
    // such route won't work with OpenAPI, since HTTP method is not specified
    route "/" <| htmlString "go to <a href='/swagger'>/swagger</a>"
]

let notFoundHandler (ctx: HttpContext) =
    let logger = ctx.GetLogger()
    logger.LogWarning("Unhandled 404 error")
    ctx.Write <| NotFound {| Error = "Resource was not found" |}

let errorHandler (ctx: HttpContext) (next: RequestDelegate) =
    task {
        try
            return! next.Invoke(ctx)
        with
        | :? ModelBindException
        | :? RouteParseException as ex ->
            let logger = ctx.GetLogger()
            logger.LogWarning(ex, "Unhandled 400 error")
            return! ctx.Write <| BadRequest {| Error = ex.Message |}
        | ex ->
            let logger = ctx.GetLogger()
            logger.LogError(ex, "Unhandled 500 error")
            ctx.SetStatusCode StatusCodes.Status500InternalServerError
            return! ctx.WriteText <| string ex
    }
    :> Task


let configureApp (appBuilder: IApplicationBuilder) =
    appBuilder
        .UseRouting()
        .UseAuthorization()
        .Use(errorHandler)
        .UseSwaggerUI(fun options -> options.SwaggerEndpoint("/openapi/v1.json", "v1"))
        .UseOxpecker(endpoints)
        .Run(notFoundHandler)

type BasicAuthenticationOptions() =
    inherit AuthenticationSchemeOptions()

type BasicAuthenticationHandler(options: IOptionsMonitor<BasicAuthenticationOptions>,
                                logger: ILoggerFactory,
                                encoder: UrlEncoder) =
    inherit AuthenticationHandler<BasicAuthenticationOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        let authHeader = this.Request.Headers["Authorization"].ToString()
        task {
            if String.IsNullOrWhiteSpace(authHeader) || not (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) then
                return AuthenticateResult.Fail("Missing or invalid Authorization header")
            else
                let encodedCredentials = authHeader.Substring("Basic ".Length).Trim()
                let credentialsBytes = Convert.FromBase64String(encodedCredentials)
                let credentials = Encoding.UTF8.GetString(credentialsBytes).Split(':')
                return
                    match credentials with
                    | [| username ; password |] when validate_merchant username password -> 
                        let claims = [ Claim(ClaimTypes.Name, username) ]
                        let identity = ClaimsIdentity(claims, this.Scheme.Name)
                        let principal = ClaimsPrincipal(identity)
                        let ticket = AuthenticationTicket(principal, this.Scheme.Name)
                        AuthenticateResult.Success(ticket)
                    | [|_ ; _ |] -> 
                        AuthenticateResult.Fail("Invalid username or password")
                    | _ -> 
                        AuthenticateResult.Fail("Invalid credentials format")
        }

let configureServices (services: IServiceCollection) =
    services
        .AddAuthentication("BasicAuthentication")
            .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>("BasicAuthentication", null)
        |> ignore
    services
        .AddAuthorization()
        .AddRouting()
        .AddOxpecker()
        .AddOpenApi(fun options ->
            options.AddDocumentTransformer(fun document context cancelationToken -> 
                let basicAuthScheme = OpenApiSecurityScheme(Name = "Authorization", Scheme = "Basic", Type = SecuritySchemeType.Http, In = ParameterLocation.Header, Description = "Basic authentication")
                if document.Components = null then
                    document.Components <- OpenApiComponents()
                document.Components.SecuritySchemes.Add("BasicAuthentication", basicAuthScheme)
                
                let basicAuthSchemeRef = OpenApiSecurityScheme(Reference = OpenApiReference(Id = "BasicAuthentication", Type = ReferenceType.SecurityScheme))
                let basicAuthRequirement = OpenApiSecurityRequirement()
                basicAuthRequirement.Add(basicAuthSchemeRef, [| |])
                document.SecurityRequirements.Add(basicAuthRequirement)
                Task.CompletedTask
                ) |> ignore)
    |> ignore

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    configureServices builder.Services
    let app = builder.Build()
    configureApp app
    app.MapOpenApi() |> ignore // for json OpenAPI endpoint
    app.Run()
    0