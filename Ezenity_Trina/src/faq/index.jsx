import React from "react";
import { Route, Switch } from "react-router-dom";

import { Overview } from './Overview';

function FAQ({ match }) {
  const { path } = match;

  return (
    <div className="p-4">
      <div className="container">
          <Switch>
              <Route exact path={path} component={Overview} />
          </Switch>
      </div>
    </div>
  );
}

export { FAQ };