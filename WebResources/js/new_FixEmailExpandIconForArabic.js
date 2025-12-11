function fixEmailExpandIconForArabic(executionContext) {
    console.log("✔ Auto Email Expand script started...");

    const intervalId = setInterval(() => {
        const iframe = document.querySelector("iframe[data-id='notescontrol']");

        if (iframe && iframe.contentDocument) {
            console.log("✔ Timeline iframe found!");
            clearInterval(intervalId);

            const iframeDoc = iframe.contentDocument;
            observeTimelineInIframe(iframeDoc);
        } else {
            console.warn("⚠ Waiting for timeline iframe...");
        }
    }, 2000);
}

function observeTimelineInIframe(doc) {
    const container = doc.querySelector("[id*='timelinewallBody']");

    if (!container) {
        console.warn("⚠ Timeline container not found inside iframe.");
        return;
    }

    console.log("✔ Observing timeline inside iframe...");

    expandEmails(doc);

    const observer = new MutationObserver(() => expandEmails(doc));
    observer.observe(container, { childList: true, subtree: true });
}

function expandEmails(doc) {
    const emails = doc.querySelectorAll("span.symbolFont.NewEmail-symbol");

    emails.forEach(icon => {
        const recordId = icon.id.replace("timeline_record_default_icons", "");
        const expandArrow = doc.querySelector(`#timeline_record_expand_arrow_container${recordId}`);

        if (expandArrow && expandArrow.getAttribute("aria-expanded") === "false") {
            expandArrow.click();
            console.log(`✔ Expanded email: ${recordId}`);
        }
    });
}
