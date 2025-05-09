module Tests

open System
open Xunit
open Payment

open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit


let defaultTestMerchants = [| 
    { merchant_id = "grmklt123"; merchant_key = "1234567890" };
    { merchant_id = "hxeqnd852"; merchant_key = "1234567890" }; 
|]

type ValidMerchant = ValidMerchant of string with
    member x.Get = match x with ValidMerchant r -> r
    override x.ToString() = x.Get
    static member op_Explicit(ValidMerchant i) = i
    static member merchantExists = 
        fun (ValidMerchant m) -> merchantExists m    
    
    static member validMerchantArb () = 
        Gen.elements defaultTestMerchants |> Gen.map (fun m -> m.merchant_id)
        |> Arb.fromGen
        |> Arb.convert ValidMerchant ValidMerchant.op_Explicit
        |> Arb.filter ValidMerchant.merchantExists

type InvalidMerchant = InvalidMerchant of string with
    member x.Get = match x with InvalidMerchant r -> r
    override x.ToString() = x.Get
    static member op_Explicit(InvalidMerchant i) = i

[<Properties( Arbitrary=[| typeof<ValidMerchant> |] )>]
module AcceptinRequestsProperties =
    
    merchants <- defaultTestMerchants

    [<Property>]
    let acceptingPaymentIntentProduceSameResults (merchant_id: string) (intent: PaymentIntent) (qty: uint8) = 
        int qty <> 0 ==> 
            let transaction_id = accept_payment_intent merchant_id (getDate()) intent
            let produced_same_transactions = 
                Seq.init (int qty) (fun _ -> accept_payment_intent merchant_id (getDate()) intent)
                |> Seq.forall (fun transaction_id2 -> transaction_id = transaction_id2)
            produced_same_transactions
    [<Property>]
    let afterAcceptTransactionInStatusCreate (merchant_id: ValidMerchant) (intent: PaymentIntent)  = 
        let date = getDate()
        let transaction_id = accept_payment_intent merchant_id.Get date intent
        let transaction = transaction_id |> Result.bind getTransaction 
        match transaction with
        | Ok t -> 
            t.merchant_id = merchant_id.Get && t.intent = intent && t.status = Created
        | Error _ -> false

    [<Property>]
    let invalidMerchantProduceError (merchant_id: InvalidMerchant) (intent: PaymentIntent) = 
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent
        match transaction_id with
        | Ok _ -> false
        | Error error_code -> 
            match error_code with
            | MerchantNotFound -> true
            | _ -> false

    [<Property>]
    let differentMerchantsProduceDifferentTransactions (merchant_id1: ValidMerchant) (merchant_id2: ValidMerchant) (intent: PaymentIntent) =
        merchant_id1 <> merchant_id2 ==>     
            let transaction_id1 = accept_payment_intent merchant_id1.Get (getDate()) intent
            let transaction_id2 = accept_payment_intent merchant_id2.Get (getDate()) intent
            transaction_id1 <> transaction_id2

[<Fact>]
let ``My test`` () =
    merchants <- defaultTestMerchants
    let date = getDate()
    let intent = 
        { amount = 0L;
            currency = "";
            order_id = "";
            description = "";
            payment_type = "";
            user_id = "";
            email = "";
            phone = "";
            success_url = "";
            failure_url = "";
            callback_url = "";
            payment_lifetime = 0L;
            lang = "";
            metadata = Map.empty }
    let transaction_id = accept_payment_intent "hxeqnd852" date intent
    let transaction = transaction_id |> Result.bind getTransaction 
    let result = 
        match transaction with
        | Ok t -> 
            t.date = date && t.merchant_id = "hxeqnd852" && t.intent = intent && t.status = Created
        | Error _ -> false
    Assert.True(result)
