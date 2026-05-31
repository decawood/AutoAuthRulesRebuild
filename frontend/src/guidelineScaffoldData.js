export const objectiveGuidelineScaffold = {
  id: 'pneumonia-m282',
  code: 'M-282',
  title: 'Pneumonia',
  version: '29.0',
  focus: 'Objective indication agreement',
  summary:
    'Mock XML-like indication tree for scaffolding the future guideline renderer. Replace this fixture when Daniel provides the actual guideline XML objects.',
  metrics: {
    metAi: 74,
    confidence: 97,
    agreementAgree: 80,
    agreementDisagree: 20,
    recall: 89
  },
  nodes: [
    {
      id: 'admission-one-or-more',
      type: 'group',
      text: 'Admission is indicated for 1 or more of the following:',
      requirement: '1 or more',
      metrics: {
        metAi: 74,
        confidence: 97,
        agreementAgree: 80,
        agreementDisagree: 20,
        recall: 89
      },
      items: [
        {
          id: 'hypoxemia',
          type: 'indication',
          text: 'Hypoxemia',
          selected: true,
          metrics: {
            metAi: 30,
            confidence: 97,
            agreementAgree: 99,
            agreementDisagree: 1,
            recall: 93
          }
        },
        {
          id: 'hemodynamic-instability',
          type: 'indication',
          text: 'Hemodynamic instability',
          metrics: {
            metAi: 5,
            confidence: 98,
            agreementAgree: 90,
            agreementDisagree: 10,
            recall: 81
          }
        },
        {
          id: 'altered-mental-status',
          type: 'indication',
          text: 'Altered mental status that is severe or persistent',
          metrics: {
            metAi: 3,
            confidence: 99,
            agreementAgree: 70,
            agreementDisagree: 30,
            recall: 76
          }
        },
        {
          id: 'ventilatory-assistance',
          type: 'indication',
          text: 'Ventilatory assistance needed (eg, mechanical ventilation, noninvasive ventilation)',
          metrics: {
            metAi: 3,
            confidence: 99,
            agreementAgree: 80,
            agreementDisagree: 20,
            recall: 88
          }
        },
        {
          id: 'bacteremia',
          type: 'indication',
          text: 'Bacteremia',
          metrics: {
            metAi: 4,
            confidence: 97,
            agreementAgree: 50,
            agreementDisagree: 50,
            recall: 62
          }
        },
        {
          id: 'moderate-risk-category',
          type: 'indication',
          text: 'Moderate-risk-category patient (Pneumonia Severity Index Class IV or V, or CURB-65 score of 3 or greater)',
          selected: true,
          metrics: {
            metAi: 15,
            confidence: 99,
            agreementAgree: 99,
            agreementDisagree: 1,
            recall: 94
          }
        },
        {
          id: 'outpatient-treatment-failure',
          type: 'indication',
          text: 'Failure of outpatient treatment',
          metrics: {
            metAi: 1,
            confidence: 95,
            agreementAgree: 91,
            agreementDisagree: 9,
            recall: 72
          }
        },
        {
          id: 'persistent-respiratory-finding',
          type: 'indication',
          text: 'Respiratory finding (eg Tachypnea) that persists despite observation treatment',
          selected: true,
          metrics: {
            metAi: 3,
            confidence: 96,
            agreementAgree: 98,
            agreementDisagree: 2,
            recall: 84
          }
        },
        {
          id: 'complicated-pleural-effusions',
          type: 'indication',
          text: 'Complicated pleural effusions (eg empyema, loculated)',
          selected: true,
          metrics: {
            metAi: 8,
            confidence: 99,
            agreementAgree: 99,
            agreementDisagree: 1,
            recall: 90
          }
        },
        {
          id: 'poor-outcome-risk-factor',
          type: 'group',
          text: 'Presence of a risk factor for poor outcome',
          requirement: '1 or more',
          metrics: {
            metAi: 4,
            confidence: 97,
            agreementAgree: 89,
            agreementDisagree: 11,
            recall: 79
          },
          items: [
            {
              id: 'gross-hemoptysis',
              type: 'indication',
              text: 'Gross hemoptysis',
              metrics: {
                metAi: 1,
                confidence: 96,
                agreementAgree: 88,
                agreementDisagree: 12,
                recall: 78
              }
            },
            {
              id: 'cavitary-infiltrate',
              type: 'indication',
              text: 'Cavitary infiltrate',
              metrics: {
                metAi: 2,
                confidence: 97,
                agreementAgree: 91,
                agreementDisagree: 9,
                recall: 81
              }
            },
            {
              id: 'immunocompromised-state',
              type: 'indication',
              text: 'Immunocompromised state',
              selected: true,
              metrics: {
                metAi: 1,
                confidence: 98,
                agreementAgree: 94,
                agreementDisagree: 6,
                recall: 85
              }
            }
          ]
        }
      ]
    }
  ]
};
