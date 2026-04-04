import PropTypes from 'prop-types';
import React from 'react';
import Modal from 'Components/Modal/Modal';
import UploadModalContent from './UploadModalContent';

function UploadModal({ isOpen, onModalClose }) {
  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <UploadModalContent
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

UploadModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default UploadModal;
