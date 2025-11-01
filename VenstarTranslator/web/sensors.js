// sensors.js - Sensor CRUD operations and table management

let sensors = [];
let currentEditingSensor = null;
let isAddingNewSensor = false;
let sortColumn = 'sensorID';
let sortDirection = 'asc';

/**
 * Initialize the page when DOM is loaded
 */
document.addEventListener('DOMContentLoaded', function() {
  loadSensors();
  setupSortableHeaders();
});

/**
 * Load sensors from API and render table
 */
function loadSensors() {
  fetch('/api/sensors')
    .then(response => response.json())
    .then(data => {
      sensors = data;
      renderTable();
    })
    .catch(error => {
      console.error('Error loading sensors:', error);
      showResponseModal(
        '<i class="fas fa-exclamation-triangle text-danger me-2"></i>Failed to load sensors',
        'Error'
      );
    });
}

/**
 * Setup click handlers for sortable column headers
 */
function setupSortableHeaders() {
  const headers = document.querySelectorAll('th.sortable');
  headers.forEach(header => {
    header.addEventListener('click', function() {
      const column = this.getAttribute('data-sort');

      // Toggle direction if clicking same column
      if (sortColumn === column) {
        sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
      } else {
        sortColumn = column;
        sortDirection = 'asc';
      }

      // Update sort icons
      updateSortIcons();

      // Re-render table with new sort
      renderTable();
    });

    // Add pointer cursor
    header.style.cursor = 'pointer';
  });
}

/**
 * Update sort icons in table headers
 */
function updateSortIcons() {
  const headers = document.querySelectorAll('th.sortable');
  headers.forEach(header => {
    const column = header.getAttribute('data-sort');
    const icon = header.querySelector('.sort-icon');

    if (column === sortColumn) {
      icon.className = sortDirection === 'asc' ? 'fas fa-sort-up sort-icon' : 'fas fa-sort-down sort-icon';
    } else {
      icon.className = 'fas fa-sort sort-icon';
    }
  });
}

/**
 * Sort sensors array by current sort column and direction
 */
function sortSensors() {
  sensors.sort((a, b) => {
    let aVal = a[sortColumn];
    let bVal = b[sortColumn];

    // Handle different data types
    if (typeof aVal === 'string') {
      aVal = aVal.toLowerCase();
      bVal = bVal.toLowerCase();
    }

    if (aVal < bVal) {
      return sortDirection === 'asc' ? -1 : 1;
    }
    if (aVal > bVal) {
      return sortDirection === 'asc' ? 1 : -1;
    }
    return 0;
  });
}

/**
 * Render the sensors table
 */
function renderTable() {
  sortSensors();

  const tbody = document.getElementById('sensors-body');
  tbody.innerHTML = '';

  sensors.forEach(sensor => {
    const row = createTableRow(sensor);
    tbody.appendChild(row);
  });

  updateSortIcons();

  // Initialize Bootstrap tooltips
  const tooltipTriggerList = document.querySelectorAll('[data-bs-toggle="tooltip"]');
  [...tooltipTriggerList].map(tooltipTriggerEl => new bootstrap.Tooltip(tooltipTriggerEl));
}

/**
 * Create a table row for a sensor
 */
function createTableRow(sensor) {
  const tr = document.createElement('tr');

  // Name column
  const nameTd = document.createElement('td');
  nameTd.textContent = sensor.name;
  tr.appendChild(nameTd);

  // Sensor ID column
  const idTd = document.createElement('td');
  idTd.innerHTML = `<span class="badge bg-primary sensor-id">${sensor.sensorID}</span>`;
  tr.appendChild(idTd);

  // Status column
  const statusTd = document.createElement('td');
  let statusHtml = '';

  if (sensor.enabled) {
    statusHtml = '<span class="badge status-enabled"><i class="fas fa-circle-check me-1"></i>Enabled</span>';
  } else {
    statusHtml = '<span class="badge status-disabled"><i class="fas fa-circle-xmark me-1"></i>Disabled</span>';
  }

  // Add problem indicator if there's an issue
  if (sensor.hasProblem && sensor.enabled) {
    const lastBroadcast = sensor.lastSuccessfulBroadcast
      ? new Date(sensor.lastSuccessfulBroadcast).toLocaleString()
      : 'Never';
    statusHtml += '<br><span class="badge status-problem mt-1" data-bs-toggle="tooltip" data-bs-placement="top" ' +
      `title="Last successful broadcast: ${lastBroadcast}. Check server logs for details.">` +
      '<i class="fas fa-exclamation-triangle me-1"></i>Problem</span>';
  }

  statusTd.innerHTML = statusHtml;
  tr.appendChild(statusTd);

  // Purpose column
  const purposeTd = document.createElement('td');
  purposeTd.innerHTML = renderPurposeBadge(sensor.purpose);
  tr.appendChild(purposeTd);

  // Actions column
  const actionsTd = document.createElement('td');
  actionsTd.innerHTML = renderActionButtons(sensor);
  tr.appendChild(actionsTd);

  return tr;
}

/**
 * Render purpose badge HTML
 */
function renderPurposeBadge(purpose) {
  let icon = '';
  let badgeClass = 'badge';

  switch(purpose.toLowerCase()) {
    case 'outdoor':
      icon = '<i class="fas fa-tree me-1"></i>';
      badgeClass += ' purpose-outdoor';
      break;
    case 'remote':
      icon = '<i class="fas fa-home me-1"></i>';
      badgeClass += ' purpose-remote';
      break;
    case 'return':
      icon = '<i class="fas fa-arrow-rotate-left me-1"></i>';
      badgeClass += ' purpose-return';
      break;
    case 'supply':
      icon = '<i class="fas fa-wind me-1"></i>';
      badgeClass += ' purpose-supply';
      break;
    default:
      icon = '<i class="fas fa-question me-1"></i>';
      badgeClass += ' purpose-remote';
  }

  return `<span class="${badgeClass}">${icon}${purpose}</span>`;
}

/**
 * Render action buttons HTML
 */
function renderActionButtons(sensor) {
  let buttons = '<div class="d-grid gap-2">';

  buttons += `<button class="btn btn-warning btn-sm" onclick="editSensor(${sensor.sensorID})">` +
               '<i class="fas fa-edit me-1"></i>Edit</button>';

  buttons += `<button class="btn btn-danger btn-sm" onclick="deleteSensor(${sensor.sensorID})">` +
               '<i class="fas fa-trash me-1"></i>Delete</button>';

  buttons += `<button class="btn btn-info btn-sm" onclick="getLatestTemperature('${sensor.sensorID}')">` +
               '<i class="fas fa-thermometer-half me-1"></i>Get Temperature</button>';

  if (sensor.enabled) {
    buttons += `<button class="btn btn-primary btn-sm" onclick="sendPairingPacket('${sensor.sensorID}')">` +
                   '<i class="fas fa-wifi me-1"></i>Send Pairing Packet</button>';
  }

  buttons += '</div>';
  return buttons;
}

/**
 * Add a new sensor - opens modal with empty form
 */
function addNewSensor() {
  isAddingNewSensor = true;
  currentEditingSensor = null;

  // Clear and populate form with defaults
  document.getElementById('edit-name').value = '';
  document.getElementById('edit-enabled').checked = true;
  document.getElementById('edit-purpose').value = 'Remote';
  document.getElementById('edit-scale').value = 'F';
  document.getElementById('edit-url').value = '';
  document.getElementById('edit-ignoreSSLErrors').checked = false;
  document.getElementById('edit-jsonPath').value = '';

  // Clear headers
  document.getElementById('headers-container').innerHTML = '';

  // Show modal
  showEditModal('Add New Sensor');
}

/**
 * Edit existing sensor - opens modal with pre-filled form
 * @param {number} sensorID - ID of sensor to edit
 */
function editSensor(sensorID) {
  isAddingNewSensor = false;

  // Find the sensor data
  const sensor = sensors.find(s => s.sensorID === sensorID);
  if (!sensor) {
    showResponseModal(
      '<i class="fas fa-exclamation-triangle text-danger me-2"></i>Sensor not found',
      'Error'
    );
    return;
  }

  currentEditingSensor = sensor;

  // Populate the form
  document.getElementById('edit-name').value = sensor.name;
  document.getElementById('edit-enabled').checked = sensor.enabled;
  document.getElementById('edit-purpose').value = sensor.purpose;
  document.getElementById('edit-scale').value = sensor.scale;
  document.getElementById('edit-url').value = sensor.url;
  document.getElementById('edit-ignoreSSLErrors').checked = sensor.ignoreSSLErrors;
  document.getElementById('edit-jsonPath').value = sensor.jsonPath;

  // Clear and populate headers
  const headersContainer = document.getElementById('headers-container');
  headersContainer.innerHTML = '';
  if (sensor.headers && sensor.headers.length > 0) {
    sensor.headers.forEach((header, index) => {
      addHeaderRow(header.name, header.value);
    });
  }

  // Show modal
  showEditModal('Edit Sensor Configuration');
}

/**
 * Add a header row to the headers container
 * @param {string} name - Header name
 * @param {string} value - Header value
 */
function addHeaderRow(name = '', value = '') {
  const container = document.getElementById('headers-container');
  const headerCount = container.children.length;

  if (headerCount >= 5) {
    showResponseModal(
      '<i class="fas fa-exclamation-triangle text-warning me-2"></i>Maximum 5 headers allowed',
      'Warning'
    );
    return;
  }

  const headerRow = document.createElement('div');
  headerRow.className = 'header-row row g-2 mb-2';
  headerRow.innerHTML = `
    <div class="col">
      <input type="text" class="form-control form-control-sm header-name" value="${name}" placeholder="Header name">
    </div>
    <div class="col">
      <input type="text" class="form-control form-control-sm header-value" value="${value}" placeholder="Header value">
    </div>
    <div class="col-auto">
      <button type="button" class="btn btn-danger btn-sm" onclick="removeHeaderRow(this)">
        <i class="fas fa-trash"></i>
      </button>
    </div>
  `;

  container.appendChild(headerRow);
}

/**
 * Remove a header row
 * @param {HTMLElement} button - The remove button that was clicked
 */
function removeHeaderRow(button) {
  button.closest('.header-row').remove();
}

/**
 * Save sensor (create or update)
 */
function saveSensor() {
  const form = document.getElementById('editForm');

  // Validate form
  if (!form.checkValidity()) {
    form.reportValidity();
    return;
  }

  // Collect form data
  const formData = {
    name: document.getElementById('edit-name').value,
    enabled: document.getElementById('edit-enabled').checked,
    purpose: document.getElementById('edit-purpose').value,
    scale: document.getElementById('edit-scale').value,
    url: document.getElementById('edit-url').value,
    ignoreSSLErrors: document.getElementById('edit-ignoreSSLErrors').checked,
    jsonPath: document.getElementById('edit-jsonPath').value,
    headers: []
  };

  // Collect headers
  const headerRows = document.querySelectorAll('#headers-container .header-row');
  headerRows.forEach(row => {
    const name = row.querySelector('.header-name').value.trim();
    const value = row.querySelector('.header-value').value.trim();
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

  fetch(url, {
    method: method,
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(formData)
  })
  .then(response => {
    if (!response.ok) {
      return response.json().then(err => Promise.reject(err));
    }
    return response.json();
  })
  .then(data => {
    // Reload the table data
    loadSensors();
    const action = isAddingNewSensor ? 'added' : 'updated';
    showResponseModal(
      '<i class="fas fa-check-circle text-success me-2"></i>Sensor ' + action + ' successfully!',
      'Success'
    );
    hideEditModal();
  })
  .catch(error => {
    let errorMessage = error.message || 'Unknown error';
    showResponseModal(
      '<i class="fas fa-exclamation-triangle text-danger me-2"></i>' + errorMessage,
      'Error'
    );
  });
}

/**
 * Delete a sensor
 * @param {number} sensorId - ID of sensor to delete
 */
function deleteSensor(sensorId) {
  confirmDialog(
    'Are you sure you want to delete this sensor?',
    function() {
      fetch('/api/sensors/' + sensorId, {
        method: 'DELETE'
      })
      .then(response => {
        if (!response.ok) {
          return response.json().then(err => Promise.reject(err));
        }
        return response.json();
      })
      .then(data => {
        // Reload the table data
        loadSensors();
        showResponseModal(
          '<i class="fas fa-check-circle text-success me-2"></i>Sensor deleted successfully!',
          'Success'
        );
      })
      .catch(error => {
        let errorMessage = error.message || 'Unknown error';
        showResponseModal(
          '<i class="fas fa-exclamation-triangle text-danger me-2"></i>' + errorMessage,
          'Error'
        );
      });
    },
    {
      title: 'Confirm Delete',
      yesText: 'Delete',
      noText: 'Cancel',
      yesClass: 'btn-danger'
    }
  );
}

/**
 * Send pairing packet for a sensor
 * @param {string} sensorID - ID of sensor
 */
function sendPairingPacket(sensorID) {
  const button = event.target.closest('button');
  button.classList.add('disabled');
  button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Sending...';

  fetch('/api/sensors/' + sensorID + '/pair')
    .then(response => {
      if (!response.ok) {
        return response.json().then(err => Promise.reject(err));
      }
      return response.json();
    })
    .then(data => {
      showResponseModal(
        '<i class="fas fa-check-circle text-success me-2"></i>' + data.message,
        'Success'
      );
    })
    .catch(error => {
      let msg = error.message || 'Unknown error';
      showResponseModal(
        '<i class="fas fa-exclamation-triangle text-danger me-2"></i>' + msg,
        'Error'
      );
    })
    .finally(() => {
      button.classList.remove('disabled');
      button.innerHTML = '<i class="fas fa-wifi me-1"></i>Send Pairing Packet';
    });
}

/**
 * Get latest temperature for a sensor
 * @param {string} sensorID - ID of sensor
 */
function getLatestTemperature(sensorID) {
  const button = event.target.closest('button');
  button.classList.add('disabled');
  button.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Loading...';

  fetch('/api/sensors/' + sensorID + "/latest")
    .then(response => {
      if (!response.ok) {
        return response.json().then(err => Promise.reject(err));
      }
      return response.json();
    })
    .then(data => {
      showResponseModal(
        '<i class="fas fa-thermometer-half text-primary me-2"></i>' +
        '<strong>' + data.temperature + "°" + data.scale + '</strong>',
        'Latest Temperature'
      );
    })
    .catch(error => {
      let msg = error.message || 'Unknown error';
      showResponseModal(
        '<i class="fas fa-exclamation-triangle text-danger me-2"></i>' + msg,
        'Error'
      );
    })
    .finally(() => {
      button.classList.remove('disabled');
      button.innerHTML = '<i class="fas fa-thermometer-half me-1"></i>Get Temperature';
    });
}
