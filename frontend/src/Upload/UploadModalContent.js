import PropTypes from 'prop-types';
import React, { Component, createRef } from 'react';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';

class UploadModalContent extends Component {

  constructor(props, context) {
    super(props, context);

    this.state = {
      selectedFiles: [],
      isUploading: false,
      uploadResults: null,
      errorMessage: null,
      isDragOver: false
    };

    this._fileInputRef = createRef();
  }

  //
  // Listeners

  onSelectFilesPress = () => {
    this._fileInputRef.current.click();
  };

  onFileInputChange = (event) => {
    const files = Array.from(event.target.files);
    this.setState({ selectedFiles: files, uploadResults: null, errorMessage: null });
  };

  onDragOver = (event) => {
    event.preventDefault();
    this.setState({ isDragOver: true });
  };

  onDragLeave = () => {
    this.setState({ isDragOver: false });
  };

  onDrop = (event) => {
    event.preventDefault();
    const files = Array.from(event.dataTransfer.files);
    this.setState({ selectedFiles: files, isDragOver: false, uploadResults: null, errorMessage: null });
  };

  onUploadPress = async () => {
    const { selectedFiles } = this.state;

    if (!selectedFiles.length) {
      return;
    }

    this.setState({ isUploading: true, errorMessage: null, uploadResults: null });

    const formData = new FormData();
    selectedFiles.forEach((file) => {
      formData.append('file', file);
    });

    try {
      const response = await fetch('/api/v1/upload', {
        method: 'POST',
        headers: {
          'X-Api-Key': window.Readarr.apiKey
        },
        body: formData
      });

      if (response.ok) {
        const result = await response.json();
        this.setState({
          isUploading: false,
          uploadResults: result,
          selectedFiles: []
        });
        if (this._fileInputRef.current) {
          this._fileInputRef.current.value = '';
        }
      } else {
        const error = await response.text();
        this.setState({ isUploading: false, errorMessage: error || `Error ${response.status}` });
      }
    } catch (err) {
      this.setState({ isUploading: false, errorMessage: err.message });
    }
  };

  //
  // Render

  render() {
    const {
      onModalClose
    } = this.props;

    const {
      selectedFiles,
      isUploading,
      uploadResults,
      errorMessage,
      isDragOver
    } = this.state;

    const dropZoneStyle = {
      border: `2px dashed ${isDragOver ? '#5d9cec' : '#ccc'}`,
      borderRadius: '4px',
      padding: '40px 20px',
      textAlign: 'center',
      cursor: 'pointer',
      backgroundColor: isDragOver ? 'rgba(93,156,236,0.1)' : 'transparent',
      marginBottom: '16px'
    };

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          Upload Book File
        </ModalHeader>

        <ModalBody>
          <div
            style={dropZoneStyle}
            onDragOver={this.onDragOver}
            onDragLeave={this.onDragLeave}
            onDrop={this.onDrop}
            onClick={this.onSelectFilesPress}
          >
            <input
              ref={this._fileInputRef}
              type="file"
              multiple={true}
              accept=".epub,.mobi,.azw,.azw3,.pdf,.cbz,.cbr,.lit,.djvu,.fb2"
              style={{ display: 'none' }}
              onChange={this.onFileInputChange}
            />
            {
              selectedFiles.length > 0 ? (
                <div>
                  <div><strong>Selected files:</strong></div>
                  {selectedFiles.map((f, i) => (
                    <div key={i}>{f.name} ({(f.size / 1024 / 1024).toFixed(2)} MB)</div>
                  ))}
                </div>
              ) : (
                <div>
                  <div>Drag and drop book files here, or click to select</div>
                  <div style={{ marginTop: '8px', color: '#888', fontSize: '12px' }}>
                    Supported: EPUB, MOBI, AZW, PDF, CBZ, CBR, LIT, DJVU, FB2
                  </div>
                </div>
              )
            }
          </div>

          {
            uploadResults &&
              <div style={{ color: '#5bb85d', marginBottom: '8px' }}>
                <strong>Upload successful!</strong>
                <div>Saved to: {uploadResults.folder}</div>
                {uploadResults.files.map((f, i) => (
                  <div key={i}>{f.name}</div>
                ))}
                <div style={{ marginTop: '8px', color: '#888', fontSize: '12px' }}>
                  Use Manual Import (Wanted &rarr; Missing &rarr; Manual Import) to import the file.
                </div>
              </div>
          }

          {
            errorMessage &&
              <div style={{ color: '#d9534f' }}>
                Upload failed: {errorMessage}
              </div>
          }
        </ModalBody>

        <ModalFooter>
          <Button
            onPress={onModalClose}
          >
            Close
          </Button>

          <SpinnerButton
            kind={kinds.PRIMARY}
            isSpinning={isUploading}
            isDisabled={!selectedFiles.length || isUploading}
            onPress={this.onUploadPress}
          >
            Upload
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

UploadModalContent.propTypes = {
  onModalClose: PropTypes.func.isRequired
};

export default UploadModalContent;
