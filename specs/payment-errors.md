# I-Business Payment API Error Codes Specification

## Overview
Comprehensive error code reference for I-Business payment API operations. Error codes provide standardized identification of issues across all payment lifecycle states and API methods, enabling proper error handling, debugging, and user experience optimization.

## Error Code Structure
- **Code**: Numeric identifier (0 = success, non-zero = error)
- **Message**: Brief, user-friendly error description
- **Details**: Optional technical details for debugging
- **Context**: Applicable API methods and payment states

## Success Code
| Code | Message | Details | Description |
|------|---------|---------|-------------|
| 2000 | Operation completed successfully | - | Successful operation |

## System and Configuration Errors

### Core System Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1001 | Переданные параметры не соответствуют требуемому формату | - | Parameter mapping failed |
| 1002 | Отсутствуют обязательные параметры в запросе | - | Required parameters missing |
| 1003 | Критическая ошибка платежной системы | - | Internal acquiring system error |
| 1004 | Статус операции не может быть изменен | - | Invalid state transition attempted |
| 1005 | Требуется обращение в службу технической поддержки | - | Support contact required |

### Card and Customer Management Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1006 | Не удалось привязать карту к профилю клиента | - | Card binding failed |
| 1007 | Текущий статус клиента блокирует выполнение операции | - | Invalid customer status |
| 1008 | Статус транзакции не позволяет выполнить запрашиваемое действие | - | Invalid transaction status |

### URL and Routing Errors  
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1009 | Не предоставлен URL для перенаправления пользователя | - | Redirect URL empty |
| 1010 | Операция списания заблокирована для данного терминала | - | Charge method blocked |
| 1011 | Обработка платежа в данный момент невозможна | - | Payment execution impossible |
| 1012 | Указан некорректный срок действия ссылки перенаправления | - | Invalid redirect expiration |

### Payment Method Availability
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1013 | Мобильные платежи находятся на техническом обслуживании | - | Mobile payment unavailable |
| 1014 | Оплата через WebMoney недоступна для данного терминала | - | WebMoney payment unavailable |
| 1015 | Переданные данные платежа не прошли валидацию | - | Invalid payment |
| 1016 | Ошибка при обработке платежа через EINV | - | EINV payment failed |
| 1017 | Сформированный счет был отклонен системой | - | Invoice rejected |
| 1018 | Предоставленные данные не соответствуют требованиям | - | Invalid input data |
| 1019 | Не удалось обработать MasterCard транзакцию | - | MasterCard payment failed |
| 1020 | Оплата через WebMoney завершилась ошибкой | - | WebMoney payment failed |
| 1021 | Операция с указанным идентификатором заказа уже выполняется | - | Duplicate order ID error |
| 1022 | Ошибка подключения к ACQAPI сервису | - | ACQAPI service error |

### System Maintenance and Service Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1023 | Кассовое оборудование не может быть повторно активировано | - | Cash register link reactivation unavailable |
| 1024 | Ошибка при отправке уведомления | - | Notification sending failed |
| 1025 | Не удалось отправить электронное письмо | - | Email sending failed |
| 1026 | Ошибка при отправке SMS-сообщения | - | SMS sending failed |
| 1027 | Требуется обращение к продавцу для решения проблемы | - | Contact merchant |
| 1028 | Повторное прохождение 3DS проверки недопустимо | - | Repeat 3DS not allowed |
| 1029 | Сервис временно недоступен, попробуйте позднее | Не обнаружено оплаченных назначений платежа | No paid payment assignments found |

### Document and Receipt Service Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1030 | Доступ к документам по прямой ссылке ограничен | Доступ к документам по ссылке заблокирован | Document URL access forbidden |
| 1031 | Требуется указать один из параметров: список email адресов или URL | Укажите один из параметров: список email или URL | Email or URL required |
| 1032 | Ограниченные права доступа к документам для данного системного идентификатора | Доступ к документам ограничен для идентификатора системы | System ID document access forbidden |
| 1033 | Операция с указанным идентификатором отсутствует в системе | Запрашиваемая операция не найдена | Operation not found |
| 1034 | Параметры запроса содержат некорректные данные | Неправильные параметры в отправленном запросе | Invalid request data |
| 1035 | Критическая ошибка при формировании документа | Невозможно сформировать документ. Повторите попытку | Document generation failed |
| 1036 | Не удалось сгенерировать документ, повторите операцию | Доступ к документам ограничен для терминала | Document generation retry required |
| 1037 | Система генерации документов временно недоступна | Документ временно недоступен для формирования | Document generation temporary failure |
| 1038 | Служба формирования документов недоступна | Внешний сервис не отвечает | External service unavailable |

### Foreign Card Restrictions
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 131 | Карты зарубежных банков не принимаются | Карты зарубежных банков не принимаются. Воспользуйтесь картой российского банка | Foreign card operation unavailable |
| 222 | Оплата картами иностранных банков заблокирована | Оплата картами иностранных банков заблокирована. Используйте карту РФ | Foreign card payment unavailable |
| 313 | Перевод на иностранную карту запрещен | Перевод на иностранную карту невозможен. Используйте карту РФ | Foreign card payout unavailable |
| 404 | Возврат на зарубежную карту невозможен | Возврат на зарубежную карту недоступен. Консультация специалиста | Foreign card refund unavailable |

## Payment Processing Errors

### General Payment Processing
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 859 | Повторите операцию. При повторении ошибки свяжитесь с поддержкой | - | Multiple scenarios (see variations) |
| 140 | Ошибка проверки 3DS аутентификации | Невозможно подтвердить подлинность карты | 3DS authentication failed |
| 251 | Необходимо обратиться в службу поддержки | - | Multiple scenarios (see variations) |
| 342 | На счете недостаточно денежных средств | - | Insufficient account funds |
| 433 | Невозможно выполнить регулярный платеж | - | Recurring payment error |
| 524 | Необходимо сконфигурировать автоматические Maestro платежи | - | Maestro autopayment setup required |

### Card and 3DS Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 615 | Карта не поддерживает 3DS верификацию. Используйте другую карту | - | Card doesn't support 3DS |
| 706 | Некорректный идентификатор карты. Проверьте, что карта была привязана | - | Invalid CardId |
| 817 | Оплата доступна исключительно для 3DS карт. Воспользуйтесь другой картой | - | 3DS-only payment required |
| 908 | Отсутствует идентификатор 3DS сессии | - | 3DS transaction ID not found |
| 199 | Не получен ответ 3DS проверки | - | 3DS challenge response missing |
| 280 | Некорректный ответ 3DS аутентификации | - | Invalid 3DS challenge response |

### Insufficient Funds and Limits
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 371 | На карте недостаточно денежных средств | - | Insufficient card funds |
| 462 | Превышен лимит попыток подтверждения операции | - | Authorization attempt limit exceeded |
| 553 | Операция временно недоступна, повторите через некоторое время | - | Temporary processing issue |
| 644 | Повторите операцию позднее | - | Temporary processing issue |
| 735 | Операция невозможна, повторите попытку | - | Temporary processing issue |
| 826 | Некорректное состояние договора, обратитесь к менеджеру | - | Invalid contract status |

## Validation Errors

### Required Field Validation
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 917 | Обязательное поле PaymentId не заполнено | - | PaymentId required |
| 117 | Обязательное поле paymentMethod не заполнено | - | Payment method required |
| 208 | Обязательное поле paymentObject не заполнено | - | Payment object required |
| 359 | Обязательное поле measurementUnit не заполнено | - | Measurement unit required |
| 440 | Терминал заблокирован или неактивен | - | Terminal blocked |
| 531 | Параметры запроса не могут быть пустыми | - | Request parameters required |

### Authentication Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 622 | Ошибка аутентификации. Проверьте TerminalKey и SecretKey | - | Invalid token |
| 713 | Недействительные учетные данные. Проверьте TerminalKey | Терминал с указанным ключом отсутствует | Terminal not found |
| 804 | Поле email не может быть незаполненным | - | Email required |

### Data Size Validation
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 895 | Параметр DATA превысил максимальный допустимый размер | - | DATA parameter too large |
| 186 | Слишком длинное имя ключа в параметре DATA | - | DATA key name too long |
| 277 | Слишком длинное значение ключа в параметре DATA | - | DATA key value too long |

### Field Size Validation
| Code | Message Template | Context |
|------|------------------|---------|
| 368 | Некорректная длина поля TerminalKey: допустимо от {min} до {max} символов | Terminal key size validation |
| 459 | Некорректный формат IP-адреса | IP format validation |
| 540 | Некорректная длина поля OrderId: допустимо от {min} до {max} символов | Order ID size validation |
| 631 | Некорректная длина поля Description: допустимо от {min} до {max} символов | Description size validation |
| 722 | Некорректное значение поля Currency: максимум {value} | Currency validation |
| 813 | Некорректная длина поля PayForm: допустимо от {min} до {max} символов | Payment form size validation |
| 904 | Некорректная длина поля CustomerKey: допустимо от {min} до {max} символов | Customer key size validation |

### Numeric and Format Validation
| Code | Message | Context |
|------|---------|---------|
| 195 | Некорректный формат поля PaymentId | Payment ID format |
| 286 | Номер карты должен содержать только цифры | Card PAN validation |
| 377 | Некорректно указан период действия карты | Card expiry validation |
| 468 | Некорректная длина поля CardHolder: допустимо от {min} до {max} | Card holder validation |
| 559 | Код CVV должен состоять только из цифр | CVV validation |
| 640 | Некорректный формат адреса электронной почты | Email format validation |
| 731 | Недопустимо малая сумма. Минимум {value} копеек | Amount validation |
| 822 | Карта просрочена | Card expiry validation |
| 913 | Валюта {value} не поддерживается данным терминалом | Currency restriction |

## Receipt and Fiscal Errors

### Receipt Validation
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 114 | Общая стоимость товаров в чеке не соответствует сумме оплаты | - | Receipt total mismatch |
| 205 | Обязательное поле Receipt не заполнено | - | Receipt required |
| 356 | Количество значащих цифр после запятой в Quantity не должно превышать {value} | - | Quantity precision validation |
| 447 | Ошибка оформления чека в фискальной системе | - | Receipt service registration error |
| 538 | Ошибка получения чека из фискальной системы | - | Receipt service retrieval error |
| 629 | Ошибка регистрации организации в фискальной системе | - | Organization creation error |
| 720 | Ошибка настройки кассового оборудования | - | Cash register creation error |
| 811 | Кассовое оборудование отсутствует | - | Cash register not found |

### Agent and Supplier Information
| Code | Message | Context |
|------|---------|---------|
| 902 | Некорректное значение поля agentSign | Agent sign validation |
| 193 | Обязательное поле AgentSign не заполнено | Agent sign required |
| 284 | Обязательное поле SupplierInfo не заполнено | Supplier info required |
| 375 | Обязательное поле Inn в SupplierInfo не заполнено | Supplier INN required |

### Marketplace and Operations
| Code | Message | Context |
|------|---------|---------|
| 466 | Поле Fee в Shops должно иметь неотрицательное значение | Shop fee validation |
| 557 | Поле Amount в Shops должно быть не меньше 1 | Shop amount validation |
| 648 | Сумма чека не соответствует сумме платежа | Receipt/payment amount mismatch |
| 739 | Заказ {value} не обнаружен для терминала {value} | Order not found |

### Marked Goods Validation
| Code | Message | Context |
|------|---------|---------|
| 830 | Необходимо указать markProcessingMode для маркированной продукции | Mark processing mode required |
| 921 | Необходимо указать markCode для маркированной продукции | Mark code required |
| 112 | Необходимо указать markQuantity для маркированной продукции | Mark quantity required |
| 203 | Поле markQuantity применимо исключительно для маркированных товаров | Mark quantity for marked goods only |
| 354 | markQuantity используется только для расчета частичного количества | Mark quantity for fractional calculation |

## Service and Limit Errors (400-699)

### Internal Service Errors
| Code | Message | Context |
|------|---------|---------|
| 2001 | Критическая внутренняя ошибка системы обработки платежей | Internal system error |
| 2002 | Сервис временно недоступен, повторите операцию через некоторое время | Retry later |
| 2003 | Достигнут месячный лимит по количеству операций пополнения | Monthly topup limit exceeded |
| 2004 | Превышен лимит суммы пополнения через бесконтактные платежные системы | Contactless topup limit exceeded |
| 2005 | Превышен лимит суммы пополнения с использованием виртуальной карты | Virtual card topup limit exceeded |
| 2006 | Достигнут месячный лимит суммы пополнения через мобильное приложение | Mobile app monthly limit exceeded |

### Certificate Management
| Code | Message | Context |
|------|---------|---------|
| 2011 | Запрашиваемый сертификат отсутствует в системе | Certificate not found |
| 2012 | Срок действия сертификата уже истек | Certificate expired |
| 2013 | Сертификат был отозван или заблокирован | Certificate blocked |
| 2014 | Сертификат ранее уже был сохранен для указанного терминала | Certificate already saved |
| 2015 | Срок действия сертификата еще не наступил | Certificate not yet valid |
| 2017 | Не удалось обработать сертификат | Certificate processing error |

### Card Binding Errors
| Code | Message | Context |
|------|---------|---------|
| 2020 | Привязка карты к указанному терминалу заблокирована системными настройками | Card binding forbidden |
| 2021 | Терминал с указанным идентификатором отсутствует в системе | Terminal not found |
| 2022 | Карта с указанным ключом запроса не обнаружена в системе | Card by request key not found |
| 2023 | Идентификатор клиента не найден в базе данных | Customer key not found |
| 2024 | Тестовый платеж при привязке карты завершился неудачей | Payment during card binding failed |
| 2025 | Критическая ошибка при попытке привязать карту к профилю клиента | Card binding internal error |
| 2026 | Карта заблокирована и внесена в список запрещенных к использованию | Card blacklisted |
| 2027 | Данная карта не поддерживает технологию 3DS аутентификации | Card doesn't support 3DS |
| 2028 | Указан некорректный номер банковской карты | Invalid card number |
| 2030 | Карта уже привязана к указанному идентификатору клиента | Card already bound |
| 2031 | 3DS верификация карты не была успешно завершена | 3DS verification failed |
| 2034 | Сумма тестового холдирования указана некорректно | Invalid hold amount |

### Card and Limit Restrictions
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 2040 | Карта заблокирована банком-эмитентом и внесена в черный список | - | Card blacklisted |
| 2041 | Мерчант отклонил проведение операции по указанной карте | - | Merchant rejected card |
| 2042 | Данный терминал принимает только карты MasterCard | - | MasterCard only |
| 2043 | Достигнут максимально допустимое количество попыток оплаты с данной карты | - | Card attempt limit exceeded |
| 2044 | Не предоставлены обязательные персональные данные отправителя | Не переданы персональные данные отправителя для операции emoney2card больше 15000 руб. | Sender data required for large transfers |
| 2045 | Сумма операции должна быть больше нуля | Сумма операции не может быть равна 0 | Amount cannot be zero |
| 2046 | Сумма операции превышает допустимые лимиты | Лимит на сумму пополнения emoney2card | Operation amount limit exceeded |
| 2047 | Номер карты содержит ошибки или не прошел проверку | Карта не прошла проверку по алгоритму Луна | Card failed Luhn check |
| 2048 | Интернет-магазин неактивен или заблокирован администрацией | - | Shop blocked or not activated |

## Bank Response Errors

### Bank Communication Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 3001 | Обратитесь в банк-эмитент для решения вопроса | Необходимо обратиться в банк, выпустивший карту, для авторизации платежа | Contact issuing bank |
| 3002 | Некорректный идентификатор торговой точки | Предоставленный номер мерчанта не распознан банковской системой | Invalid merchant ID |
| 3003 | Операция заблокирована системой безопасности банка-эмитента | - | Suspicious payment |
| 3004 | Банк-эмитент отклонил проведение транзакции | Банк-эмитент отклонил авторизацию операции | Bank rejected payment |

### Card Validation Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 3005 | Предоставленные реквизиты карты не прошли валидацию | Проверьте правильность введенных данных карты или используйте другую карту | Invalid card |
| 3006 | Номер банковской карты содержит ошибки | Проверьте правильность введенного номера карты | Invalid card number |
| 3007 | Срок действия карты завершился | - | Card expired |
| 3008 | Карта просрочена и не может быть использована | Проверьте дату окончания действия карты или воспользуйтесь актуальной картой | Card expired |
| 3009 | Некорректно указана дата окончания действия карты | - | Incorrect expiry date entered |
| 3010 | Код безопасности CVV/CVC не соответствует карте | Проверьте правильность ввода трехзначного кода с оборотной стороны карты | Invalid CVV |

### Insufficient Funds and Limits
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 3011 | Сумма операции превышает доступные лимиты по карте | Размер платежа превышает допустимые лимиты. Используйте другую карту или обратитесь в банк | Amount exceeds card limit |
| 3012 | На банковском счету недостаточно средств для совершения операции | Для совершения платежа необходимо пополнить баланс карты | Insufficient card funds |
| 3013 | Достигнут максимальный лимит по количеству операций по карте | - | Customer payment limit exceeded |
| 3014 | Превышен допустимый лимит транзакций по карте | - | Customer payment limit exceeded |
| 3015 | Исчерпан суточный лимит по количеству платежей | - | Daily payment limit exceeded |

### Card Security and Fraud
| Code | Message | Context |
|------|---------|---------|
| 3016 | Карта заблокирована по заявлению о ее утере | Card reported lost |
| 3017 | Клиент установил ограничения на проведение подобных операций | Customer restricted operations |
| 3018 | Операции данного типа заблокированы владельцем карты | Customer restricted operations |
| 3019 | Операция заблокирована системой мониторинга мошенничества | Suspicious payment |
| 3020 | Система безопасности отметила операцию как подозрительную | Suspicious payment |

### System and Processing Errors
| Code | Message | Context |
|------|---------|---------|
| 3021 | Получен неопределенный статус от банковской системы | Unknown payment status |
| 3022 | Авторизационный токен утратил свою актуальность | Token expired |
| 3023 | Операция успешно завершена | Operation successful |
| 3024 | Критическая ошибка в банковской системе | System error |
| 3025 | Выбранный способ оплаты временно недоступен | Payment method disabled |

### Merchant Limit Errors
| Code | Message | Context |
|------|---------|---------|
| 3026 | Размер операции превышает максимально допустимую сумму для разового платежа | Single operation limit exceeded |
| 3027 | Превышены лимиты по сумме или количеству операций для данного торгового поинта | Operation limits exceeded |
| 3028 | Исчерпан допустимый суточный оборот по терминалу | Daily turnover limit reached |
| 3029 | Прием карт выпущенных в данной стране запрещен для этого мерчанта | Country card restriction |


## BNPL and Installment Errors (7000-7999)

### Buy Now Pay Later Errors
| Code | Message | Context |
|------|---------|---------|
| 7149 | Рассрочная система временно недоступна | Installment operation forbidden |
| 7332 | Сервис кредитного брокера не отвечает. Попробуйте позднее | Credit broker unavailable |
| 7581 | Покупка частями заблокирована для данной операции | BNPL operation forbidden |
| 7798 | Система отложенных платежей находится на обслуживании | BNPL service unavailable |
| 7023 | Превышен кредитный лимит для рассрочного платежа | Installment credit limit exceeded |
| 7456 | Клиент не прошел скоринговую проверку для BNPL | BNPL scoring check failed |
| 7687 | Товар не подходит для покупки в рассрочку | Product not eligible for installments |
| 7291 | Минимальная сумма для отложенного платежа не достигнута | BNPL minimum amount not met |
| 7834 | Максимальное количество активных рассрочек превышено | Active installment limit exceeded |
| 7512 | Неподтвержденный номер телефона для BNPL | Phone verification required for BNPL |
| 7643 | Документы клиента требуют дополнительной проверки | Customer documents need additional verification |
| 7178 | Возраст клиента не соответствует требованиям кредитования | Customer age doesn't meet credit requirements |
| 7925 | Регион клиента не обслуживается системой рассрочек | Customer region not supported for installments |
| 7367 | Банк-партнер отклонил заявку на рассрочку | Partner bank declined installment application |
| 7754 | Превышено время ожидания ответа от кредитной системы | Timeout waiting for credit system response |

## System Errors (9000-9999)

### Critical System Errors
| Code | Message | Context |
|------|---------|---------|
| 9001 | Попробуйте повторить попытку позже | Temporary system issue |
| 9999 | Внутренняя ошибка системы | Internal system error |

### Custom Payment Gateway Errors (9002-9009)
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 9002 | Терминал недоступен или деактивирован | Terminal access is denied or terminal is deactivated | Terminal access denied - merchant not found or inactive |
| 9003 | Ошибка аутентификации токена | Token authentication failed - verify TerminalKey and signature generation | Token authentication failed - invalid SHA-256 signature |
| 9004 | Платежная операция не найдена | Payment record not found in the system | Payment record not found - invalid PaymentId or access denied |
| 9005 | Ошибка валидации запроса | Request validation failed - check required fields and formats | Request validation failed - FluentValidation errors |
| 9006 | Операция с данным OrderId уже существует | Order with this OrderId already exists for this terminal | Duplicate order operation - OrderId must be unique per terminal |
| 9007 | Внутренняя ошибка обработки запроса | Internal request processing error occurred | Internal processing error - system exception during operation |
| 9008 | Недопустимый статус платежа для подтверждения | Payment must be in AUTHORIZED status to perform confirmation | Invalid payment status for operation - state transition not allowed |
| 9009 | Сумма подтверждения превышает авторизованную сумму | Requested amount exceeds authorized amount for confirmation | Operation amount limit exceeded - partial confirmation amount too large |

## Receipt-Specific Validation Errors

### Marked Goods Validation
| Error Message | Context |
|---------------|---------|
| Количество товара должно быть больше нуля | Item quantity validation |
| Максимальная длина rawcode — 223 символа | Raw code length validation |
| Касса принимает значение markCode только с типом rawcode | Mark code type validation |
| Для данной кассы ожидалось одно из itemCode или markСode | Code requirement validation |
| Для данной кассы ожидалось либо itemCode, либо markCode | Exclusive code validation |
| Для данной кассы предусмотрена передача только markСode | Mark code only validation |

## Error Handling Best Practices

### Error Categories by Severity

#### Critical Errors (Immediate Action Required)
- System errors (3, 401, 9999)
- Terminal blocked (202)
- Invalid authentication (204, 205)

#### User Action Required
- Insufficient funds (103, 116, 1051)
- Card expired (252, 1033, 1054)
- Invalid card data (1014, 1015, 1082)

#### Configuration Issues
- Terminal not configured (3001, 3019)
- Payment method unavailable (13, 1099)
- Certificate issues (411-417)

#### Temporary Issues (Retry Recommended)
- Service unavailable (55, 120, 123, 125, 402)
- Bank processing issues (99, 1017, 1030, 1034, 1089)
- External service errors (96, 97, 98)

#### Business Logic Errors
- Invalid state transitions (4, 8)
- Amount validation (251, 308, 334)
- Limit exceeded (119, 403-406, 1013, 1061, 1065)

### Error Response Pattern

#### Standard Error Structure
```json
{
  "Success": false,
  "ErrorCode": "1051",
  "Message": "Недостаточно средств на карте",
  "Details": "Не получилось оплатить. На карте недостаточно средств",
  "Status": "REJECTED"
}
```

#### Error Context Enhancement
```json
{
  "Success": false,
  "ErrorCode": "204",
  "Message": "Неверный токен. Проверьте пару TerminalKey/SecretKey",
  "Details": "Token validation failed",
  "Status": "ERROR",
  "ErrorContext": {
    "category": "AUTHENTICATION",
    "severity": "CRITICAL",
    "retry_possible": false,
    "user_action_required": true,
    "support_contact": true
  }
}
```

### Integration Guidelines

#### Error Handling Strategy by API Method

**Init Method Error Handling:**
- 204, 205: Check terminal credentials
- 201, 202: Validate required parameters
- 251: Verify amount format and minimum
- 253: Check currency support

**CheckOrder Method Error Handling:**
- 255: Payment not found - verify PaymentId
- 335: Order not found - verify OrderId/TerminalKey pair
- 204: Authentication failure

**Cancel Method Error Handling:**
- 4, 8: Invalid payment status for cancellation
- 330: Cancel amount exceeds original
- 3002: Insufficient merchant balance for refund
- 100: Multiple cancellation scenarios

**Confirm Method Error Handling:**
- 4: Payment not in AUTHORIZED status
- 330: Confirm amount exceeds authorized
- 100: Payment already confirmed
- 255: Payment not found

#### Client-Side Error Handling

**Critical Errors (Stop Processing):**
```javascript
const criticalErrors = ['3', '202', '204', '205', '501', '648'];
if (criticalErrors.includes(response.ErrorCode)) {
    // Stop processing, contact support
    showCriticalError(response.Message);
    return;
}
```

**Retry Logic:**
```javascript
const retryableErrors = ['55', '120', '123', '125', '402', '999', '1017', '1030'];
if (retryableErrors.includes(response.ErrorCode)) {
    // Implement exponential backoff retry
    setTimeout(() => retryOperation(), getBackoffDelay());
}
```

**User Action Required:**
```javascript
const userActionErrors = ['103', '116', '252', '1014', '1015', '1051', '1082'];
if (userActionErrors.includes(response.ErrorCode)) {
    // Show user-friendly message with action required
    showUserActionRequired(response.Message);
}
```

#### Server-Side Error Monitoring

**Alert Thresholds:**
- Error rate > 5% for critical errors (3, 401, 9999)
- Authentication failure rate > 10% (204, 205)
- Payment failure rate > 15% (100-199 range)
- SBP error rate > 20% (3000-5999 range)

**Error Categorization for Monitoring:**
```json
{
  "error_categories": {
    "system_errors": ["3", "401", "9999"],
    "authentication_errors": ["204", "205"],
    "validation_errors": ["201-262", "308-386"],
    "bank_rejections": ["1000-1999"],
    "insufficient_funds": ["103", "116", "1051"],
    "card_issues": ["252", "506", "508", "1014", "1015"],
    "limit_exceeded": ["403-406", "1013", "1061", "1204"],
    "sbp_errors": ["3000-5999"],
    "receipt_errors": ["308-386"]
  }
}
```

### Localization and User Experience

#### Error Message Localization
```json
{
  "error_translations": {
    "ru": {
      "1051": "Недостаточно средств на карте",
      "1082": "Неверный CVV код"
    },
    "en": {
      "1051": "Insufficient funds on card",
      "1082": "Invalid CVV code"
    }
  }
}
```

#### User-Friendly Error Mapping
```json
{
  "user_friendly_messages": {
    "1051": {
      "title": "Недостаточно средств",
      "message": "На вашей карте недостаточно средств для совершения покупки",
      "action": "Попробуйте другую карту или пополните счет",
      "icon": "warning"
    },
    "1082": {
      "title": "Неверный код безопасности",
      "message": "Проверьте правильность ввода CVV/CVC кода",
      "action": "Введите трехзначный код с обратной стороны карты",
      "icon": "error"
    }
  }
}
```

### Error Analytics and Reporting

#### Key Error Metrics
- **Error Rate by Category**: System/Authentication/Validation/Bank
- **Error Distribution by Payment Method**: Card/SBP/BNPL
- **Geographic Error Patterns**: Foreign cards restrictions
- **Temporal Error Patterns**: Peak hours error rates
- **Merchant-Specific Errors**: Terminal configuration issues

#### Error Correlation Analysis
```json
{
  "error_correlations": {
    "high_3ds_failures": {
      "related_errors": ["101", "106", "108", "511"],
      "common_cause": "3DS not properly configured"
    },
    "foreign_card_issues": {
      "related_errors": ["76", "77", "78", "79"],
      "common_cause": "Foreign card restrictions"
    },
    "receipt_validation_cluster": {
      "related_errors": ["308", "309", "334"],
      "common_cause": "Fiscal integration issues"
    }
  }
}
```

### Compliance and Regulatory Considerations

#### PCI DSS Compliance
- Never log sensitive data in error messages
- Mask card numbers in error contexts
- Secure error log storage and access

#### Financial Regulations
- Maintain audit trail of all error occurrences
- Report suspicious activity patterns (fraud-related errors)
- Comply with data retention requirements for error logs

#### Customer Protection
- Provide clear, actionable error messages
- Avoid exposing internal system details
- Offer alternative payment methods when possible

This comprehensive error code specification provides the foundation for robust error handling across the entire I-Business payment lifecycle, ensuring proper system monitoring, user experience optimization, and regulatory compliance.

=====
