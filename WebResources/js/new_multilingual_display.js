function setMultilingualDisplayName(executionContext) {
    console.log("✅ Function Started: setMultilingualDisplayName");

    // Get form context
    var formContext = executionContext.getFormContext();
    console.log("ℹ️ formContext initialized:", formContext);

    // Get user language
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;
    console.log("🌐 Current user languageId:", userLang);

    // Get attributes
    var nameEnAttr = formContext.getAttribute("new_name_en");
    var nameArAttr = formContext.getAttribute("new_name_ar");
    var displayAttr = formContext.getAttribute("name");

    console.log("🔍 nameEnAttr:", nameEnAttr ? "Found" : "Not Found");
    console.log("🔍 nameArAttr:", nameArAttr ? "Found" : "Not Found");
    console.log("🔍 displayAttr:", displayAttr ? "Found" : "Not Found");

    if (!displayAttr) {
        console.error("❌ Display field 'name' not found. Exiting function.");
        return;
    }

    // Get field values
    var nameEn = nameEnAttr ? nameEnAttr.getValue() : "";
    var nameAr = nameArAttr ? nameArAttr.getValue() : "";

    console.log("📄 English Name (new_name_en):", nameEn);
    console.log("📄 Arabic Name (new_name_ar):", nameAr);

    // Decide which value to show
    var displayValue;
    if (userLang === 1025 && nameAr) {
        displayValue = nameAr;
        console.log("✅ Arabic selected based on user language.");
    } else {
        displayValue = nameEn;
        console.log("✅ English selected as fallback/default.");
    }

    console.log("➡️ Setting Display Name (name):", displayValue);

    // Update display field
    displayAttr.setValue(displayValue);
    displayAttr.setSubmitMode("always");
    console.log("✅ Display Name updated and submit mode set to 'always'.");
	 

}

function onNameChange(executionContext) {
    console.log("🔄 onNameChange triggered. Re-running display name logic...");
    setMultilingualDisplayName(executionContext);
	
	
}
