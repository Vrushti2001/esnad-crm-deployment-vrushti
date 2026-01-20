 function toggleOtherTextOnSubClassification2(executionContext) {
    console.log("🚀 Function triggered: toggleOtherTextOnSubClassification2");

    var formContext = executionContext.getFormContext();
    console.log("✔ Form context received");

    var otherTextCtrl = formContext.getControl("new_othertext");
    if (!otherTextCtrl) {
        console.error("❌ Control not found: new_othertext");
        return;
    }

    // Hide by default
    otherTextCtrl.setVisible(false);
    console.log("🙈 new_othertext hidden by default");

    var subClassAttr = formContext.getAttribute("new_subclassification2");
    if (!subClassAttr || !subClassAttr.getValue()) {
        console.log("ℹ new_subclassification2 empty");
        return;
    }

    var subClassId = subClassAttr.getValue()[0].id.replace(/[{}]/g, "");
    console.log("🆔 Subclassification2 record ID:", subClassId);

    Xrm.WebApi.retrieveRecord(
        "new_subclassification2",
        subClassId,
        "?$select=new_englishsubclassification2"
    ).then(
        function (result) {
            var value = result.new_englishsubclassification2;

            console.log("📄 Raw value from CRM:", value);

            if (!value) {
                console.log("⚠ Value is null");
                return;
            }

            // ✅ CASE-INSENSITIVE SAFE COMPARISON
            var normalizedValue = value.trim().toLowerCase();
            console.log("🔎 Normalized value:", normalizedValue);

            if (normalizedValue === "other") {
                console.log("✅ Value matched 'other' → showing new_othertext");
                otherTextCtrl.setVisible(true);
            } else {
                console.log(
                    "❌ Value is not 'other' (" + normalizedValue + ") → keeping hidden"
                );
            }
        },
        function (error) {
            console.error("🔥 Error retrieving subclassification2:", error.message);
        }
    );
}
