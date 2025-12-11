function enableContainsSearchOnCustomer(executionContext) {
    console.log("✅ Function enableContainsSearchOnCustomer triggered.");

    var formContext = executionContext.getFormContext();
    var customerControl = formContext.getControl("customerid");

    if (!customerControl) {
        console.error("❌ Customer lookup control not found.");
        return;
    }

    console.log("✔ Customer lookup control found: customerid");

    customerControl.addPreSearch(function () {
        console.log("🔍 PreSearch event triggered for Customer lookup.");

        // Instead of using user input, apply a wide filter
        var fetchXmlFilter = `
            <filter type="and">
                <condition attribute="name" operator="like" value="%%" />
            </filter>`;

        console.log("📄 Applying generic FetchXML filter (%%).");
        customerControl.addCustomFilter(fetchXmlFilter, "account");
        console.log("✅ Custom filter applied for Account entity.");
    });
}
