function setMultilingualDisplayName(executionContext) {
    var formContext = executionContext.getFormContext();
    var userLang = Xrm.Utility.getGlobalContext().userSettings.languageId;

    var nameEnAttr = formContext.getAttribute("new_name_en");
    var nameArAttr = formContext.getAttribute("new_name_ar");
    var displayAttr = formContext.getAttribute("name");

    if (!displayAttr) return;

    var nameEn = nameEnAttr ? nameEnAttr.getValue() : "";
    var nameAr = nameArAttr ? nameArAttr.getValue() : "";

    var displayValue = (userLang === 1025 && nameAr) ? nameAr : nameEn;
    displayAttr.setValue(displayValue);
    displayAttr.setSubmitMode("always");
}


