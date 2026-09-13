import PropTypes from 'prop-types';
import React from 'react';
import NotFound from 'Components/NotFound';
import translate from 'Utilities/String/translate';
import SeriesDetailsConnector from './SeriesDetailsConnector';

function SeriesDetailsPageConnector({ match }) {
  const { localSeriesId } = match.params;
  if (!(/^[1-9]\d*$/).test(localSeriesId || '')) {
    return <NotFound message={translate('SorryThatSeriesCannotBeFound')} />;
  }

  return <SeriesDetailsConnector key={localSeriesId} localSeriesId={localSeriesId} />;
}

SeriesDetailsPageConnector.propTypes = {
  match: PropTypes.shape({
    params: PropTypes.shape({ localSeriesId: PropTypes.string }).isRequired
  }).isRequired
};

export default SeriesDetailsPageConnector;
