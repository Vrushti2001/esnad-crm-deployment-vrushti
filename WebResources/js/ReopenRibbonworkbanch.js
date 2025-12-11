function reactivateAndSetStage(primaryControl){
    try {
        console.log("🔍 checkTeamAccess triggered");

        // List of allowed team IDs
        const allowedTeamIds = [
            "953dd3b2-544b-f011-a3fe-d4de6fab9c57",
            "2c80efda-7c4b-f011-a3ff-af212fee8ea9",
            "230121da-a673-f011-a40d-c0b1f6211923",
            "2ab2932b-9f73-f011-a40d-c0b1f6211923",
            "0eb23b1a-a967-f011-a409-87895d8b1d04",
            "16e9dd1a-b267-f011-a409-87895d8b1d04",
            "9a685a34-a967-f011-a409-87895d8b1d04"
        ];

        console.log("✔ Allowed teams:", allowedTeamIds);

        const userId = Xrm.Utility.getGlobalContext().userSettings.userId
            .replace("{", "")
            .replace("}", "");
        console.log("👤 Current User ID:", userId);

        console.log("📡 Fetching the teams of this user...");

        Xrm.WebApi.retrieveMultipleRecords(
            "teammembership",
            `?$select=teamid&$filter=systemuserid eq ${userId}`
        ).then(result => {

            console.log("📥 Raw teammembership result:", result);

            const userTeams = result.entities.map(t => t.teamid.toLowerCase());
            console.log("🏷 User is member of teams:", userTeams);

            // Check if any allowed team matches user's team list
            const isAllowed = allowedTeamIds.some(teamId =>
                userTeams.includes(teamId.toLowerCase())
            );

            console.log("🔎 Is user allowed?", isAllowed);

            if (!isAllowed) {
                console.log("❌ User NOT allowed!");
                //Xrm.Navigation.openAlertDialog({
                   // title: "Access Denied",
                    //text: "You are not allowed to perform this action."
                //});
				alert("You are not allowed to perform this action.");
                return; // ❗ STOP further execution
				primaryControl.refresh(); // Refresh form
            }
			else{
            console.log("✅ User allowed — continue execution");
			 LoadUI(primaryControl);
			 }

        }).catch(error => {
            console.error("🔥 Error while checking team:", error);
            Xrm.Navigation.openAlertDialog({
                title: "Error",
                text: "Unable to verify team access."
            });
        });

    } catch (e) {
        console.error("💥 Script Error in checkTeamAccess:", e);
    }
}


function LoadUI(primaryControl) {

    console.log("✅ Modal trigger started");
	
    const caseId = primaryControl.data.entity.getId().replace(/[{}]/g, "");
    const parentDoc = window.top.document;

   ensureBootstrapLoaded();

    const modalHtml = `
    <div id="ticketReopenModal" class="modal fade show" tabindex="-1" style="
      background-color: rgba(0,0,0,0.5);
      position: fixed;
      top: 0; left: 0;
      width: 100%; height: 100%;
      z-index: 1055;
      display: flex;
      justify-content: center;
      align-items: center;
    ">
      <div class="modal-dialog modal-dialog-centered modal-md" style="width: 100%;">
        <div class="modal-content p-3">
          <div class="modal-header">
            <h5 class="modal-title">Ticket Reopen</h5>
            <button type="button" class="btn-close" onclick="window.top.document.getElementById('ticketReopenModal').remove();"></button>
          </div>
          <div class="modal-body">
            <div class="mb-3">
              <label for="reopenComment" class="form-label">Comment</label>
              <textarea class="form-control" id="reopenComment" rows="4" placeholder="Enter comment"></textarea>
            </div>
          </div>
          <div class="modal-footer justify-content-end">
            <button class="btn btn-secondary me-2" onclick="window.top.document.getElementById('ticketReopenModal').remove();">Cancel</button>
            <button class="btn btn-primary" onclick="window.top.submitReopenModal()">Submit</button>
          </div>
        </div>
      </div>
    </div>
    `;

    const existing = parentDoc.getElementById("ticketReopenModal");
    if (existing) existing.remove();

    const container = parentDoc.createElement("div");
    container.innerHTML = modalHtml;
    parentDoc.body.appendChild(container);

    window.top.submitReopenModal = function () {
	
        const comment = window.top.document.getElementById("reopenComment").value;

        if (!comment || comment.trim() === "") {
            alert("Please enter a comment before submitting.");
            return;
        }
	 const caseId = primaryControl.data.entity.getId().replace(/[{}]/g, "");
	
        const note = {
            "new_discription": comment,
			"new_Ticket@odata.bind":`/incidents(${caseId})`
        };
		
         Xrm.WebApi.createRecord("new_incidentcomments", note).then(function (result) {
        console.log("✅ Incident Comment created:", result.id);
            console.log("✅ record created:", result.id);

            // Step 2: Reactivate Case
            Xrm.WebApi.updateRecord("incident", caseId, {
                "statecode": 0,
                "statuscode": 100000006
            }).then(function () {
                console.log("✅ Case reactivated.");

                // Step 3: Set Reopened = "Yes" (string), and set reopen datetime
                Xrm.WebApi.updateRecord("incident", caseId, {
                    "new_isreopened": "Yes",
                    "new_reopendatetime": new Date(new Date().getTime() + (3 * 60 * 60 * 1000)) // KSA = UTC+3

                }).then(function () {
                    console.log("✅ 'new_isreopened' and 'new_reopendatetime' fields updated.");

                    alert("Comment submitted and saved to Notes.");
                    window.top.document.getElementById("ticketReopenModal").remove();
                    delete window.top.submitReopenModal;

                    // Step 4: Move to new BPF stage
                    updateBPFStageAfterReopen(caseId, primaryControl);

                }, function (error) {
                    console.error("❌ Failed to update reopen fields:", error.message);
                    alert("Comment saved, but failed to update reopen fields.");
                });

            }, function (error) {
                console.error("❌ Failed to reactivate case:", error.message);
                alert("Cannot update field because case reactivation failed.");
            });

        }, function (error) {
            console.error("❌ Failed to create note:", error.message);
            alert("Failed to save note: " + error.message);
        });
    };
}

function ensureBootstrapLoaded() {
    const parentHead = window.top.document.head;
    if (!parentHead.querySelector("#bootstrap-css")) {
        const link = window.top.document.createElement("link");
        link.id = "bootstrap-css";
        link.rel = "stylesheet";
        link.href = "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";
        parentHead.appendChild(link);
    }
}

function updateBPFStageAfterReopen(caseId, formContext) {
    try {
        if (!formContext || !formContext.data) {
            console.error("❌ Form context is not available.");
            return;
        }

        const bpfEntityName = "phonetocaseprocess";
        const targetStageId = "92a6721b-d465-4d36-aef7-e8822d7a5a6a"; // Approval And Forwarding

        console.log("🔎 Fetching BPF linked to case...");

        Xrm.WebApi.retrieveMultipleRecords(bpfEntityName, `?$filter=_incidentid_value eq ${caseId}`).then(function (bpfResult) {
            if (!bpfResult.entities || bpfResult.entities.length === 0) {
                console.error("❌ No BPF instance found for this case.");
                return;
            }

            const bpfRecord = bpfResult.entities[0];
            const bpfStatus = bpfRecord["statecode"];
            const bpfId = bpfRecord["businessprocessflowinstanceid"];
            const processId = bpfRecord["_processid_value"];

            if (!bpfId || !processId) {
                console.error("❌ Missing BPF ID or Process ID.");
                alert("❌ BPF or Process ID not found. Check console.");
                return;
            }

            const updatePayload = {
                "activestageid@odata.bind": `/processstages(${targetStageId})`,
                "processid@odata.bind": `/workflows(${processId})`
            };

            const proceedToStageUpdate = () => {
                console.log("🔄 Updating BPF stage to 'Approval And Forwarding'...");

                Xrm.WebApi.updateRecord(bpfEntityName, bpfId, updatePayload).then(function () {
                    console.log("✅ BPF stage updated.");
                    formContext.data.refresh();
                }, function (error) {
                    console.error("❌ Failed to update BPF stage:", error.message);
                    alert("Failed to update BPF stage.");
                });
            };

            if (bpfStatus === 1) {
                console.log("♻️ Reactivating BPF...");

                Xrm.WebApi.updateRecord(bpfEntityName, bpfId, {
                    "statecode": 0,
                    "statuscode": 1
                }).then(function () {
                    console.log("✅ BPF reactivated.");
                    proceedToStageUpdate();
                }, function (error) {
                    console.error("❌ Failed to reactivate BPF:", error.message);
                });
            } else {
                proceedToStageUpdate();
            }

        }, function (error) {
            console.error("❌ Failed to fetch BPF:", error.message);
        });

    } catch (e) {
        console.error("❌ Exception:", e.message);
    }
}
