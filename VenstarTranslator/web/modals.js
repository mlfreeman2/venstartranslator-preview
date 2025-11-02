// modals.js - Bootstrap Modal utilities

/**
 * Show a response message in a modal
 * @param {string} message - HTML message to display
 * @param {string} title - Optional modal title (defaults to "Sensor Response")
 */
function showResponseModal(message, title = 'Sensor Response') {
  const modalElement = document.getElementById('responseModal');
  const modal = bootstrap.Modal.getOrCreateInstance(modalElement);

  document.getElementById('responseModalLabel').textContent = title;
  document.getElementById('modalMessage').innerHTML = message;

  modal.show();
}

/**
 * Show a confirmation dialog with custom callbacks
 * @param {string} message - Confirmation message
 * @param {Function} yesCallback - Function to call when user confirms
 * @param {Object} options - Optional configuration
 */
function confirmDialog(message, yesCallback, options = {}) {
  const defaults = {
    title: 'Confirm Action',
    yesText: 'Confirm',
    noText: 'Cancel',
    yesClass: 'btn-primary',
    noClass: 'btn-secondary'
  };

  const settings = { ...defaults, ...options };

  const modalElement = document.getElementById('confirmationModal');
  const modal = bootstrap.Modal.getOrCreateInstance(modalElement);

  // Set title and message
  document.getElementById('confirmationModalLabel').textContent = settings.title;
  document.getElementById('confirmation-message').innerHTML = message;

  // Configure buttons
  const yesBtn = document.getElementById('confirmYesBtn');
  const noBtn = document.getElementById('confirmNoBtn');

  yesBtn.textContent = settings.yesText;
  noBtn.textContent = settings.noText;

  // Update button classes
  yesBtn.className = `btn ${settings.yesClass}`;
  noBtn.className = `btn ${settings.noClass}`;

  // Remove old event listeners by cloning and replacing
  const newYesBtn = yesBtn.cloneNode(true);
  yesBtn.parentNode.replaceChild(newYesBtn, yesBtn);

  // Add new event listener
  newYesBtn.addEventListener('click', function() {
    modal.hide();
    if (typeof yesCallback === 'function') {
      yesCallback();
    }
  });

  modal.show();
}

/**
 * Show the edit/add sensor modal
 * @param {string} title - Modal title
 */
function showEditModal(title = 'Edit Sensor Configuration') {
  const modalElement = document.getElementById('editModal');
  const modal = bootstrap.Modal.getOrCreateInstance(modalElement);

  document.getElementById('editModalLabel').textContent = title;
  modal.show();
}

/**
 * Hide the edit modal
 */
function hideEditModal() {
  const modalElement = document.getElementById('editModal');
  const modal = bootstrap.Modal.getInstance(modalElement);
  if (modal) {
    modal.hide();
  }
}

// Clean up when modals are hidden
document.addEventListener('DOMContentLoaded', function() {
  const editModal = document.getElementById('editModal');
  if (editModal) {
    editModal.addEventListener('hidden.bs.modal', function() {
      if (typeof window.currentEditingSensor !== 'undefined') {
        window.currentEditingSensor = null;
      }
      if (typeof window.isAddingNewSensor !== 'undefined') {
        window.isAddingNewSensor = false;
      }
    });
  }
});
