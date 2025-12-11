// ===== CONFIG for restrict stage change to restricted users =====
const allowedTeamIds = [
   "953dd3b2-544b-f011-a3fe-d4de6fab9c57", // Dev D365Dev
            "2c80efda-7c4b-f011-a3ff-af212fee8ea9", // Dev Customer service team
            "230121da-a673-f011-a40d-c0b1f6211923",//Dev Service Agents Teams 
            "2ab2932b-9f73-f011-a40d-c0b1f6211923",//Dev Service Agents Department
            "0eb23b1a-a967-f011-a409-87895d8b1d04", // Prod-D365 team
            "16e9dd1a-b267-f011-a409-87895d8b1d04",  // Prod-Customer service team
            "9a685a34-a967-f011-a409-87895d8b1d04"  // Prod-Service Agents Teams
];

// Restricted stages
const restrictedStageIds = [
    "15322a8f-67b8-47fb-8763-13a28686c29d",
    "92a6721b-d465-4d36-aef7-e8822d7a5a6a",
    "65894155-4ed9-449b-ab1d-d4d4fb196e48",
    "1ee2e3b4-3e83-4fe3-9b5b-490b6e91e8af",
    "ef0a2c39-d6d9-4b29-a39b-53dc539f0982"
];

// Special stage
const specialStageId = "3b5a344f-9f9d-466b-aa08-611e60964b46";
const ProcessingStageId= "91153307-982f-479d-af7f-73048b80e52c";

let isAllowedUser = false;

// ===== MAIN ENTRY =====
window.onLoadRestrictStageByTeam = function (executionContext) {
    const formCtx = executionContext.getFormContext();
    console.log("📌 RestrictStageByTeam triggered");

    checkUserTeam(function (belongs) {
        isAllowedUser = belongs;
        console.log("✅ User allowed?", isAllowedUser);

        waitForBpfReady(formCtx, () => {
            if (!isAllowedUser) {
                enforceRestrictions(formCtx);
            } else {
                console.log("🎉 Allowed user → no restrictions applied.");
            }
        });
    });
};

// ===== HELPERS =====
function checkUserTeam(callback) {
    const userId = Xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
    Xrm.WebApi.retrieveMultipleRecords("teammembership", `?$filter=systemuserid eq ${userId}&$select=teamid`).then(
        result => {
            const belongs = result.entities.some(e => allowedTeamIds.includes(e.teamid));
            callback(belongs);
        },
        () => callback(false)
    );
}

function waitForBpfReady(formCtx, callback) {
    let retries = 0;
    const interval = setInterval(() => {
        const process = formCtx.data.process;
        if (process?.getActiveStage?.()) {
            clearInterval(interval);
            callback();
        } else if (++retries >= 20) {
            clearInterval(interval);
        }
    }, 500);
}

// Restriction logic
function enforceRestrictions(formCtx) {
    // Block Set Active
    setInterval(() => {
        const setActiveBtn = findSetActiveButton(window.top.document);
        if (!setActiveBtn || setActiveBtn.dataset.restrictApplied) return;
        setActiveBtn.dataset.restrictApplied = "true";
        const clone = setActiveBtn.cloneNode(true);
        setActiveBtn.parentNode.replaceChild(clone, setActiveBtn);
        clone.addEventListener("click", (e) => {
            e.preventDefault(); e.stopPropagation();
            alert("⚠️ You are not allowed to use Set Active.");
        }, true);
    }, 500);

    // Next Stage button restriction
    setInterval(() => {
        const nextBtn = findNextStageButton(window.top.document);
        if (!nextBtn || nextBtn.dataset.restrictApplied) return;

        nextBtn.dataset.restrictApplied = "true";
        const clone = nextBtn.cloneNode(true);
        nextBtn.parentNode.replaceChild(clone, nextBtn);

        clone.addEventListener("click", (e) => {
            const stage = formCtx.data.process.getActiveStage();
            const stageId = stage?.getId()?.replace(/[{}]/g, "").toLowerCase();

            // Special stage → open comment modal instead of moveNext
            if (stageId === specialStageId) {
                e.preventDefault(); e.stopPropagation();
                openCommentModal(formCtx);
                return;
            }
			
			if (stageId === ProcessingStageId) {
                e.preventDefault(); e.stopPropagation();
                alert("⚠️ You are not allowed to move to this stage.");
                return;
            }
            // Restricted → block
            if (restrictedStageIds.includes(stageId)) {
                e.preventDefault(); e.stopPropagation();
                alert("⚠️ You are not allowed to move to this stage.");
                return;
            }

            // Normal
            formCtx.data.process.moveNext();
        }, true);
    }, 1000);
}

// ===== Modal Logic =====
function injectBootstrapCss() {
    const head = window.top.document.head;
    if (!head.querySelector("#bootstrap-css")) {
        const link = document.createElement("link");
        link.id = "bootstrap-css";
        link.rel = "stylesheet";
        link.href = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";
        head.appendChild(link);
    }
}

function openCommentModal(formContext) {
    injectBootstrapCss();
    const doc = window.top.document;
    const modalHtml = `
        <div id="statusCommentModal" class="modal fade show" tabindex="-1" style="background-color:rgba(0,0,0,.5);position:fixed;top:0;left:0;width:100%;height:100%;z-index:1055;display:flex;justify-content:center;align-items:center;">
          <div class="modal-dialog modal-dialog-centered" style="max-width:500px;width:100%;">
            <div class="modal-content shadow-lg border-0 rounded-3">
              <div class="modal-header">
                <h5 class="modal-title">Status Change Comment</h5>
                <button type="button" class="btn-close" onclick="window.top.closeStatusCommentModal()"></button>
              </div>
              <div class="modal-body">
                <textarea id="statusCommentText" class="form-control" style="min-height:120px;" placeholder="Enter your comment..."></textarea>
              </div>
              <div class="modal-footer">
                <button class="btn btn-primary" onclick="window.top.submitStatusComment()">Submit</button>
              </div>
            </div>
          </div>
        </div>`;
    const wrapper = doc.createElement("div");
    wrapper.innerHTML = modalHtml;
    doc.body.appendChild(wrapper);
    window.top._statusCommentContext = formContext;

    window.top.submitStatusComment = function () {
        const comment = doc.getElementById("statusCommentText")?.value?.trim();
        if (!comment) return alert("Please enter a comment.");
        const caseId = formContext.data.entity.getId().replace(/[{}]/g, "");
        const statusAttr = formContext.getAttribute("statuscode");
        const statusLabel = statusAttr?.getText?.() || statusAttr?.getValue();

        const incidentComment = {
            "new_discription": `[${statusLabel}] ${comment}`,
            "new_Ticket@odata.bind": `/incidents(${caseId})`
        };

        Xrm.WebApi.createRecord("new_incidentcomments", incidentComment).then(() => {
            assignCaseToCustomerService(formContext);
            setTimeout(() => activateProcessingStage(formContext), 1000);
            window.top.closeStatusCommentModal();
        });
    };

    window.top.closeStatusCommentModal = function () {
        const modal = doc.getElementById("statusCommentModal");
        if (modal) modal.remove();
    };
}

function assignCaseToCustomerService(formContext) {
    const caseId = formContext.data.entity.getId();
    const teamGuids = [
        "fca3c311-074c-f011-a400-fbb6a348b744", // Production
        "2c80efda-7c4b-f011-a3ff-af212fee8ea9"  // Development
    ];

    function tryAssign(index) {
        if (index >= teamGuids.length) return;

        Xrm.WebApi.updateRecord("incident", caseId, {
            "ownerid@odata.bind": `/teams(${teamGuids[index]})`
        }).then(() => {
            console.log("👥 Assigned to team:", teamGuids[index]);
            formContext.data.refresh(false);
        }).catch(() => tryAssign(index + 1));
    }

    tryAssign(0);
}


// 🔁 Force Processing stage using exact ID
function activateProcessingStage(formContext) {
    const targetStageName = "Processing";

    try {
        const activePath = formContext.data.process.getActivePath();
        let targetStage = null;

        for (let i = 0; i < activePath.length; i++) {
            const s = activePath[i];
            if (s.getName().trim().toLowerCase() === targetStageName.toLowerCase()) {
                targetStage = s;
                break;
            }
        }

        if (!targetStage) {
            console.warn("⚠ Stage not found in UI path. Using Web API.");
            forceChangeViaWebAPI(formContext, targetStageName);
            return;
        }

        formContext.data.process.setActiveStage(targetStage.getId(), function (result) {
            if (result === "success") {
                console.log("✅ Stage changed to:", targetStageName);
				bindStageChangeListener(formCtx);
            } else {
                forceChangeViaWebAPI(formContext, targetStageName);
            }
        });
    } catch (err) {
        console.error("❌ Error in activateProcessingStage:", err.message);
    }
}

function forceChangeViaWebAPI(formContext, targetStageName) {
    try {
        const instanceId = formContext.data.process.getInstanceId();
        const processId = formContext.data.process.getActiveProcess().getId();
        if (!instanceId || !processId) return;

        Xrm.WebApi.retrieveRecord("workflow", processId, "?$select=uniquename").then(function (workflow) {
            const bpfEntityLogicalName = workflow.uniquename.toLowerCase();

            Xrm.WebApi.retrieveRecord(bpfEntityLogicalName, instanceId, "?$expand=processid($select=workflowid)").then(function (bpfRecord) {
                const actualProcessId = bpfRecord.processid.workflowid;

                Xrm.WebApi.retrieveMultipleRecords("processstage", `?$filter=processid/workflowid eq ${actualProcessId}`).then(function (stageResults) {
                    let matchedStage = null;
                    for (let i = 0; i < stageResults.entities.length; i++) {
                        const stage = stageResults.entities[i];
                        if (stage.stagename.trim().toLowerCase() === targetStageName.toLowerCase()) {
                            matchedStage = stage;
                            break;
                        }
                    }

                    if (!matchedStage) return;

                    const updateData = {
                        "activestageid@odata.bind": `/processstages(${matchedStage.processstageid})`
                    };

                    Xrm.WebApi.updateRecord(bpfEntityLogicalName, instanceId, updateData).then(function () {
                        console.log("✅ Stage updated via Web API.");
                    }, function (err) {
                        console.error("❌ Web API stage update failed:", err.message);
                    });
                });
            });
        });
    } catch (err) {
        console.error("❌ forceChangeViaWebAPI failed:", err.message);
    }
}

// ===== DOM Helpers =====
function findNextStageButton(doc) {
    const selectors = [
        'button[aria-label="Next Stage"]',
        'button[title="Next Stage"]',
        'button[aria-label="المرحلة التالية"]',
        'button[title="المرحلة التالية"]',
        'button[data-id="stageAdvanceAction"]',
        'button[data-id="process-next-stage"]'
    ];
    for (const sel of selectors) {
        const btn = doc.querySelector(sel);
        if (btn) return btn;
    }
    return null;
}

function findSetActiveButton(doc) {
    const selectors = [
        'button[aria-label="Set Active"]',
        'button[title="Set Active"]',
        'button[aria-label="تعيين على نشط"]',
        'button[title="تعيين على نشط"]'
    ];
    for (const sel of selectors) {
        const btn = doc.querySelector(sel);
        if (btn) return btn;
    }
    return null;
}
