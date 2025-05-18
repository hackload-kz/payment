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

type ValidPaymentIntent = ValidPaymentIntent of PaymentIntent with
    member x.Get = match x with ValidPaymentIntent r -> r
    override x.ToString() = x.Get.ToString()
    static member op_Explicit(ValidPaymentIntent i) = i
    
    static member RandomPayments () = 
        ArbMap.defaults |> ArbMap.arbitrary<PaymentIntent>
        |> Arb.convert ValidPaymentIntent ValidPaymentIntent.op_Explicit
        |> Arb.filter (fun p -> p.Get.amount > 0)

let generateMonth = Gen.choose (1, 12) 
let generateYear = Gen.choose (DateTime.Now.Year - 1 - 2000, DateTime.Now.Year + 3 - 2000) 

let generateCCV = Gen.choose (0, 999)
let generateExpiryDate =
    Gen.map2 (fun m y -> $"%02d{m}/%02d{y}") generateMonth generateYear

let generateCardNumber = 
    Gen.choose (0, 9999) |> Gen.four 
    |> Gen.filter (fun (a,b,c,d) -> 
        let checksum a = a / 1000 + (2 * (a % 1000 / 100)) % 9 + a % 100 / 10 + (2 * (a % 10)) % 9
        (checksum a + checksum b + checksum c + checksum d) % 10 = 0)
    |> Gen.map (fun (a,b,c,d) -> $"%04d{a}%04d{b}%04d{c}%04d{d}")
    

let generateCardInformation =
    Gen.map3 (fun e c n -> { card_number = n; card_holder_name = "ANDRII TESTER"; card_cvc = $"%03d{c}"; card_expiry = e })
        generateExpiryDate
        generateCCV
        generateCardNumber

type ValidCardInformation = ValidCardInformation of CardInformation with
    member x.Get = match x with ValidCardInformation r -> r
    override x.ToString() = x.Get.ToString()
    static member op_Explicit(ValidCardInformation i) = i
    
    static member RandomCards () = 
        Arb.fromGen generateCardInformation
        //ArbMap.defaults |> ArbMap.arbitrary<CardInformation>
        //|> Arb.fromGen generateCardInformation
        |> Arb.convert ValidCardInformation ValidCardInformation.op_Explicit
        |> Arb.filter (fun p -> validCard p.Get)

type InvalidMerchant = InvalidMerchant of string with
    member x.Get = match x with InvalidMerchant r -> r
    override x.ToString() = x.Get
    static member op_Explicit(InvalidMerchant i) = i

[<Properties( Arbitrary=[| typeof<ValidMerchant>; typeof<ValidPaymentIntent>; typeof<ValidCardInformation> |] )>]
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
    let afterAcceptTransactionInStatusCreate (merchant_id: ValidMerchant) (intent: ValidPaymentIntent)  = 
        let date = getDate()
        let transaction_id = accept_payment_intent merchant_id.Get date intent.Get
        let transaction = transaction_id |> Result.bind getTransaction 
        match transaction with
        | Ok t -> 
            t.merchant_id = merchant_id.Get && t.intent = intent.Get && t.status = Created
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
    let differentMerchantsProduceDifferentTransactions (merchant_id1: ValidMerchant) (merchant_id2: ValidMerchant) (intent: ValidPaymentIntent) =
        merchant_id1 <> merchant_id2 ==>     
            let transaction_id1 = accept_payment_intent merchant_id1.Get (getDate()) intent.Get
            let transaction_id2 = accept_payment_intent merchant_id2.Get (getDate()) intent.Get
            transaction_id1 <> transaction_id2

    [<Property>]
    let cannotHavePaymentIntentWithEmptyAmount (merchant_id: ValidMerchant) (intent: PaymentIntent) =
        intent.amount = 0 ==>     
            let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent
            match transaction_id with
            | Ok _ -> false
            | Error error_code -> 
                match error_code with
                | PaymentCreationError -> true
                | _ -> false

    [<Property>]
    let acceptCreditCard (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: ValidCardInformation) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate()) card.Get
            match result with
            | Ok _ -> true
            | Error _ -> false
        | Error _ -> false

    [<Property>]
    let cannnotAcceptCreditCardTwice (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: ValidCardInformation) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate()) card.Get
            match result with
            | Ok _ -> 
                let result = accept_card transaction_id (getDate()) card.Get
                match result with
                | Error CardAlreadyAccepted -> true
                | _ -> false
            | Error _ -> false
        | Error _ -> false

    [<Property>]
    let invalidCardInformationNotAccepted (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: CardInformation) =
        not (validCard card) ==>     
            let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
            match transaction_id with
            | Ok transaction_id ->
                let result = accept_card transaction_id (getDate()) card
                match result with
                | Error InvalidCardInformation -> true
                | _ -> false
            | Error _ -> false

[<Fact>]
let ``Regresson 1`` () =
    merchants <- defaultTestMerchants
    let date = getDate()
    let intent = 
        { amount = 1110L;
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
