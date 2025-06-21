let currentEditingSensor = null;
let isAddingNewSensor = false;
let table;

$(function() {
    // Initialize DataTables
    table = $('#sensors').DataTable({
        ajax: {
            url: '/api/sensors',
            dataSrc: ''
        },
        pageLength: 20,
        paging: false,
        info: false,
        searching: false,
        order: [[1, 'asc']],
        columns: [
            { data: 'name' },
            { data: 'sensorID' },
            { data: 'enabled' },
            { data: 'purpose' },
            { data: null }
        ],
        columnDefs: [
            {
                targets: 1,
                render: function(data, type, row) {
                    return '<span class="sensor-id">' + data + '</span>';
                }
            },
            {
                targets: 2,
                orderable: false,
                render: function(data, type, row) {
                    if (data) {
                        return '<span class="status-badge status-enabled"><span class="status-dot"></span>Enabled</span>';
                    } else {
                        return '<span class="status-badge status-disabled"><span class="status-dot"></span>Disabled</span>';
                    }
                }
            },
            {
                targets: 3,
                orderable: false,
                render: function(data, type, row) {
                    let icon = '';
                    let className = 'purpose-badge';
                    
                    switch(data.toLowerCase()) {
                        case 'outdoor':
                            icon = '<i class="fas fa-tree"></i>';
                            className += ' purpose-outdoor';
                            break;
                        case 'remote':
                            icon = '<i class="fas fa-home"></i>';
                            className += ' purpose-remote';
                            break;
                        case 'return':
                            icon = '<i class="fas fa-arrow-rotate-left"></i>';
                            className += ' purpose-return';
                            break;
                        case 'supply':
                            icon = '<i class="fas fa-wind"></i>';
                            className += ' purpose-supply';
                            break;
                        default:
                            icon = '<i class="fas fa-question"></i>';
                            className += ' purpose-remote';
                    }
                    
                    return '<span class="' + className + '">' + icon + ' ' + data + '</span>';
                }
            },
            {
                targets: 4,
                orderable: false,
                data: null,
                render: function(data, type, row) {
                    let buttons = '<div style="display: flex; flex-direction: column; gap: 0.5rem;">';
                    
                    buttons += '<button class="btn btn-warning" onclick="editSensor(' + row.sensorID + ')">' +
                               '<i class="fas fa-edit"></i> Edit</button>' +
                               '<button class="btn btn-error" onclick="deleteSensor(' + row.sensorID + ')">' +
                               '<i class="fas fa-trash"></i> Delete</button>';
                    if (row.enabled) {
                        buttons += '<button class="btn btn-primary" onclick="sendPairingPacket(\'' + row.sensorID + '\')">' +
                                    '<i class="fas fa-wifi"></i> Send Pairing Packet</button>' +
                                    '<button class="btn btn-secondary" onclick="getLatestTemperature(\'' + row.sensorID + '\')">' +
                                    '<i class="fas fa-thermometer-half"></i> Get Temperature</button>';
                    }
                    
                    buttons += '</div>';
                    return buttons;
                }
            }
        ]
    });
});

function addNewSensor() {
    isAddingNewSensor = true;
    currentEditingSensor = null;

    // Clear and populate form with defaults
    $('#edit-name').val('');
    $('#edit-enabled').prop('checked', true);
    $('#edit-purpose').val('Remote');
    $('#edit-scale').val('F');
    $('#edit-url').val('');
    $('#edit-ignoreSSLErrors').prop('checked', false);
    $('#edit-jsonPath').val('');

    // Clear headers
    $('#headers-container').empty();

    // Update modal title and open
    $('#editModal').dialog('option', 'title', 'Add New Sensor');
    $('#editModal').dialog('open');
}

function editSensor(sensorID) {
    isAddingNewSensor = false;
    
    // Find the sensor data directly from DataTables
    const sensor = table.rows().data().toArray().find(s => s.sensorID === sensorID);
    if (!sensor) {
        alert('Sensor not found');
        return;
    }

    currentEditingSensor = sensor;

    // Populate the form
    $('#edit-name').val(sensor.name);
    $('#edit-enabled').prop('checked', sensor.enabled);
    $('#edit-purpose').val(sensor.purpose);
    $('#edit-scale').val(sensor.scale);
    $('#edit-url').val(sensor.url);
    $('#edit-ignoreSSLErrors').prop('checked', sensor.ignoreSSLErrors);
    $('#edit-jsonPath').val(sensor.jsonPath);

    // Clear and populate headers
    $('#headers-container').empty();
    if (sensor.headers && sensor.headers.length > 0) {
        sensor.headers.forEach((header, index) => {
            addHeaderRow(header.name, header.value);
        });
    }

    // Update modal title and open
    $('#editModal').dialog('option', 'title', 'Edit Sensor Configuration');
    $('#editModal').dialog('open');
}

function addHeaderRow(name = '', value = '') {
    const container = $('#headers-container');
    const headerCount = container.children().length;
    
    if (headerCount >= 5) {
        alert('Maximum 5 headers allowed');
        return;
    }

    const headerRow = $(`
        <div class="header-row">
            <div class="form-group">
                <label class="form-label">Name</label>
                <input type="text" class="form-input header-name" value="${name}" placeholder="Header name">
            </div>
            <div class="form-group">
                <label class="form-label">Value</label>
                <input type="text" class="form-input header-value" value="${value}" placeholder="Header value">
            </div>
            <button type="button" onclick="removeHeaderRow(this)">
                <i class="fas fa-trash"></i>
            </button>
        </div>
    `);

    container.append(headerRow);
}

function removeHeaderRow(button) {
    $(button).closest('.header-row').remove();
}

function saveSensor() {
    // Collect form data
    const formData = {
        name: $('#edit-name').val(),
        enabled: $('#edit-enabled').is(':checked'),
        purpose: $('#edit-purpose').val(),
        scale: $('#edit-scale').val(),
        url: $('#edit-url').val(),
        ignoreSSLErrors: $('#edit-ignoreSSLErrors').is(':checked'),
        jsonPath: $('#edit-jsonPath').val(),
        headers: []
    };

    // Collect headers
    $('#headers-container .header-row').each(function() {
        const name = $(this).find('.header-name').val().trim();
        const value = $(this).find('.header-value').val().trim();
        if (name && value) {
            formData.headers.push({ name, value });
        }
    });

    // Determine HTTP method and URL based on whether we're adding or editing
    const method = isAddingNewSensor ? 'POST' : 'PUT';
    const url = '/api/sensors';

    // For editing, include the original sensor ID
    if (!isAddingNewSensor && currentEditingSensor) {
        formData.sensorID = currentEditingSensor.sensorID;
    }

    $.ajax({
        url: url,
        type: method,
        contentType: 'application/json',
        data: JSON.stringify(formData),
        success: function(response) {
            // Reload the table data
            table.ajax.reload();
            const action = isAddingNewSensor ? 'added' : 'updated';
            $('#modalMessage').html('<i class="fas fa-check-circle" style="color: var(--success-color); margin-right: 0.5rem;"></i>Sensor ' + action + ' successfully!');
            $('#responseModal').dialog('open');
            $('#editModal').dialog('close');
        },
        error: function(xhr, status, error) {
            let errorMessage = error;
            if (xhr.responseJSON && xhr.responseJSON.message) {
                errorMessage = xhr.responseJSON.message;
            }
            $('#modalMessage').html('<i class="fas fa-exclamation-triangle" style="color: var(--error-color); margin-right: 0.5rem;"></i>' + errorMessage);
            $('#responseModal').dialog('open');
        }
    });
}

function sendPairingPacket(sensorID) {
    const button = event.target;
    button.classList.add('loading');
    
    $.ajax({
        url: '/api/sensors/' + sensorID + '/pair',
        type: 'GET',
        success: function(response) {
            $('#modalMessage').html('<i class="fas fa-check-circle" style="color: var(--success-color); margin-right: 0.5rem;"></i>' + response.message);
            $('#responseModal').dialog('open');
        },
        error: function(xhr, status, error) {
            $('#modalMessage').html('<i class="fas fa-exclamation-triangle" style="color: var(--error-color); margin-right: 0.5rem;"></i>' + error);
            $('#responseModal').dialog('open');
        },
        complete: function() {
            button.classList.remove('loading');
        }
    });
}

function getLatestTemperature(sensorID) {
    const button = event.target;
    button.classList.add('loading');
    
    $.ajax({
        url: '/api/sensors/' + sensorID + "/latest",
        type: 'GET',
        success: function(response) {
            $('#modalMessage').html('<i class="fas fa-thermometer-half" style="color: var(--primary-color); margin-right: 0.5rem;"></i>' + 
                                    '<strong>' + response.temperature + "°" + response.scale + '</strong>');
            $('#responseModal').dialog('open');
        },
        error: function(xhr, status, error) {
            $('#modalMessage').html('<i class="fas fa-exclamation-triangle" style="color: var(--error-color); margin-right: 0.5rem;"></i>' + error);
            $('#responseModal').dialog('open');
        },
        complete: function() {
            button.classList.remove('loading');
        }
    });
}

// Generic jQuery UI confirmation dialog function
function confirmDialog(message, yesCallback, options = {}) {
    // Default options
    const defaults = {
        title: 'Confirm Action',
        width: 350,
        height: 'auto',
        modal: true,
        resizable: false,
        draggable: true,
        yesText: 'Yes',
        noText: 'No',
        yesClass: 'confirm-yes',
        noClass: 'confirm-no',
        position: { my: "center", at: "center", of: window }
    };
    
    // Merge options with defaults
    const settings = $.extend({}, defaults, options);
    
    // Set the message
    $('#confirmation-message').html(message);
    
    // Configure dialog buttons
    const buttons = {};
    buttons[settings.noText] = {
        text: settings.noText,
        class: settings.noClass,
        click: function() {
            $(this).dialog('close');
        }
    };
    buttons[settings.yesText] = {
        text: settings.yesText,
        class: settings.yesClass,
        click: function() {
            $(this).dialog('close');
            if (typeof yesCallback === 'function') {
                yesCallback();
            }
        }
    };
    
    // Destroy existing dialog if it exists
    if ($('#confirmation-dialog').hasClass('ui-dialog-content')) {
        $('#confirmation-dialog').dialog('destroy');
    }
    
    // Create and show the dialog
    $('#confirmation-dialog').dialog({
        title: settings.title,
        width: settings.width,
        height: settings.height,
        modal: settings.modal,
        resizable: settings.resizable,
        draggable: settings.draggable,
        position: settings.position,
        buttons: buttons,
        dialogClass: 'confirmation-dialog',
        close: function() {
            // Optional: Add any cleanup code here
        }
    });
}

// Demo functions
function deleteSensor(sensorId) {
    confirmDialog('Are you sure you want to delete this sensor?', function() {
        $.ajax({
            url: '/api/sensors/' + sensorId,
            type: 'DELETE',
            success: function(response) {
                // Reload the table data
                table.ajax.reload();                
                $('#modalMessage').html('<i class="fas fa-check-circle" style="color: var(--success-color); margin-right: 0.5rem;"></i>Sensor deleted successfully!');
                $('#responseModal').dialog('open');
                $('#editModal').dialog('close');
            },
            error: function(xhr, status, error) {
                let errorMessage = error;
                if (xhr.responseJSON && xhr.responseJSON.message) {
                    errorMessage = xhr.responseJSON.message;
                }
                $('#modalMessage').html('<i class="fas fa-exclamation-triangle" style="color: var(--error-color); margin-right: 0.5rem;"></i>' + errorMessage);
                $('#responseModal').dialog('open');
            }
        });
    }, {
        title: 'Confirm Delete',
        yesText: 'Delete',
        noText: 'Cancel',
        width: 400
    });
}

$(function() {
    // Response modal
    $('#responseModal').dialog({
        autoOpen: false,
        modal: true,
        width: 400,
        position: {
            my: "center",
            at: "center",
            of: window
        },
        title: "Sensor Response",
        buttons: {
            "Close": function() {
                $(this).dialog("close");
            }
        }
    });

    // Edit modal
    $('#editModal').dialog({
        autoOpen: false,
        modal: true,
        width: 600,
        maxWidth: '90vw',
        position: {
            my: "center",
            at: "center",
            of: window
        },
        title: "Edit Sensor Configuration",
        buttons: {
            "Save Changes": function() {
                // Validate form
                if (!$('#editForm')[0].checkValidity()) {
                    $('#editForm')[0].reportValidity();
                    return;
                }
                saveSensor();
            },
            "Cancel": function() {
                $(this).dialog("close");
            }
        },
        close: function() {
            currentEditingSensor = null;
            isAddingNewSensor = false;
        }
    });
});